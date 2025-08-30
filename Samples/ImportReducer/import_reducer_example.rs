// ---------------------
// --- Import Reducer --- 
// ---------------------

// Helper function to parse DbVector3 from JSON value (same as before)
// Expects either an object {"x":.., "y":.., "z":..} or array [x, y, z]
fn parse_dbvector3(val: &JsonValue) -> Result<DbVector3, String> {
    if let Some(obj) = val.as_object() {
        let x = obj.get("x").and_then(|v| v.as_f64()).ok_or_else(|| "Missing or invalid 'x' field".to_string())? as f32;
        let y = obj.get("y").and_then(|v| v.as_f64()).ok_or_else(|| "Missing or invalid 'y' field".to_string())? as f32;
        let z = obj.get("z").and_then(|v| v.as_f64()).ok_or_else(|| "Missing or invalid 'z' field".to_string())? as f32;
        Ok(DbVector3 { x, y, z })
    } else if let Some(arr) = val.as_array() {
        if arr.len() < 3 { return Err("Array must have at least 3 elements for DbVector3".to_string()); }
        let x = arr[0].as_f64().ok_or_else(|| "Element 0 ('x') not f64".to_string())? as f32;
        let y = arr[1].as_f64().ok_or_else(|| "Element 1 ('y') not f64".to_string())? as f32;
        let z = arr[2].as_f64().ok_or_else(|| "Element 2 ('z') not f64".to_string())? as f32;
        Ok(DbVector3 { x, y, z })
    } else {
        Err(format!("Expected JSON object or array for DbVector3, got different type."))
    }
}

#[spacetimedb::reducer]
pub fn import_table_data(ctx: &ReducerContext, table_name: String, json_data: String) -> Result<(), String> {
    // Optional: Add permission checks here based on ctx.sender
    // Example: Check if sender is an admin identity stored in another table
    // if !is_admin(&ctx.db, ctx.sender) { return Err("Permission Denied".to_string()); }

    log::info!("[Reducer] Attempting import for table: {}", table_name);

    // Parse the JSON data (assuming same [{schema:..., rows:...}] structure from export)
    let parsed_json: JsonValue = serde_json::from_str(&json_data)
        .map_err(|e| format!("JSON parse error: {}", e))?;
    let result_obj = parsed_json.get(0).and_then(|v| v.as_object())
        .ok_or("Invalid JSON structure: Expected array with one result object")?;
    let rows_json = result_obj.get("rows").and_then(|v| v.as_array())
        .ok_or("Invalid JSON structure: Missing 'rows' array")?;

    let mut inserted_count = 0;

    // --- Perform Delete and Insert based on table name --- 
    match table_name.as_str() {
        "config" => {
            // 1. Delete existing (Singleton with PK 0)
            // Use ctx.db handle for iter and delete
            let config_to_delete = ctx.db.config().iter().find(|c| c.id == 0);
            if let Some(config) = config_to_delete {
                ctx.db.config().delete(config); // Pass owned struct
                log::debug!("[Reducer] Deleted existing config (id 0).");
            }

            // 2. Insert new rows from JSON
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 2 { return Err(format!("Row {} for '{}' too few cols (exp 2)", idx, table_name)); }

                let id_val = row_array[0].as_u64().ok_or(format!("Row {} Col 0 ('id') not u64", idx))? as u32;
                let world_size_val = row_array[1].as_u64().ok_or(format!("Row {} Col 1 ('world_size') not u64", idx))?;

                if id_val == 0 { // Only insert the singleton ID 0
                    let new_config = Config { id: 0, world_size: world_size_val };
                    ctx.db.config().insert(new_config);
                    inserted_count += 1;
                } else {
                    log::warn!("Skipping config row {} with non-zero ID ({})", idx, id_val);
                }
            }
        }
        "entity" => {
            // 1. Delete existing
            let existing: Vec<Entity> = ctx.db.entity().iter().collect();
            log::debug!("[Reducer] Deleting {} existing '{}' entries...", existing.len(), table_name);
            for item in existing {
                ctx.db.entity().entity_id().delete(&item.entity_id);
            }

            // 2. Insert new
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 2 { return Err(format!("Row {} for '{}' too few cols (exp 2)", idx, table_name)); }

                let entity_id_val = row_array[0].as_u64().ok_or(format!("Row {} Col 0 ('entity_id') not u64", idx))? as u32;
                let pos_val = &row_array[1];
                let position = parse_dbvector3(pos_val).map_err(|e| format!("Row {} Col 1 ('position'): {}", idx, e))?;

                let new_item = Entity { entity_id: entity_id_val, position };
                ctx.db.entity().insert(new_item);
                inserted_count += 1;
            }
        }
         "mob" => {
            // 1. Delete existing
            let existing: Vec<Mob> = ctx.db.mob().iter().collect();
             log::debug!("[Reducer] Deleting {} existing '{}' entries...", existing.len(), table_name);
            for item in existing {
                ctx.db.mob().entity_id().delete(&item.entity_id);
            }

            // 2. Insert new
             for (idx, row_val) in rows_json.iter().enumerate() {
                 let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                 if row_array.len() < 2 { return Err(format!("Row {} for '{}' too few cols (exp 2)", idx, table_name)); }

                 let entity_id_val = row_array[0].as_u64().ok_or(format!("Row {} Col 0 ('entity_id') not u64", idx))? as u32;
                 let speed_val = row_array[1].as_f64().ok_or(format!("Row {} Col 1 ('speed') not f64", idx))? as f32;

                 let new_item = Mob { entity_id: entity_id_val, speed: speed_val };
                 ctx.db.mob().insert(new_item);
                 inserted_count += 1;
             }
        }
        "movement_component" => {
            // 1. Delete existing
            let existing: Vec<MovementComponent> = ctx.db.movement_component().iter().collect();
            log::debug!("[Reducer] Deleting {} existing '{}' entries...", existing.len(), table_name);
            for item in existing {
                ctx.db.movement_component().entity_id().delete(&item.entity_id);
            }

            // 2. Insert new
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 3 { return Err(format!("Row {} for '{}' too few cols (exp 3)", idx, table_name)); }

                let entity_id_val = row_array[0].as_u64().ok_or(format!("Row {} Col 0 ('entity_id') not u64", idx))? as u32;
                let direction_val = &row_array[1];
                let direction = parse_dbvector3(direction_val).map_err(|e| format!("Row {} Col 1 ('direction'): {}", idx, e))?;
                let speed_val = row_array[2].as_f64().ok_or(format!("Row {} Col 2 ('speed') not f64", idx))? as f32;
                
                let new_item = MovementComponent { entity_id: entity_id_val, direction, speed: speed_val };
                ctx.db.movement_component().insert(new_item);
                inserted_count += 1;
            }
        }
         "player" => {
            // 1. Delete existing players
            let existing_players: Vec<Player> = ctx.db.player().iter().collect();
            log::debug!("[Reducer] Deleting {} existing players...", existing_players.len());
            for player in existing_players {
                ctx.db.player().identity().delete(&player.identity);
            }
            // Optionally delete logged_out_player too?
            // let existing_logged_out: Vec<Player> = ctx.db.logged_out_player().iter().collect();
            // log::debug!("[Reducer] Deleting {} existing logged_out_players...", existing_logged_out.len());
            // for player in existing_logged_out { LoggedOutPlayer::delete_by_identity(&ctx.db, &player.identity); } 

            // 2. Insert new players
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 4 { return Err(format!("Row {} for '{}' too few cols (exp 4)", idx, table_name)); }

                 // Extract values (Handle potential array wrapping for Identity/Timestamp if needed in JSON)
                 // Assuming JSON export provides plain hex string for Identity and unix micros for Timestamp now.
                let identity_hex = row_array[0].as_str().ok_or(format!("Row {} Col 0 ('identity') not string", idx))?;
                let identity = Identity::from_hex(identity_hex).map_err(|_| format!("Row {} Col 0 invalid identity hex", idx))?;
                
                let player_id_val = row_array[1].as_u64().ok_or(format!("Row {} Col 1 ('player_id') not u64", idx))? as u32;
                let name_val = row_array[2].as_str().ok_or(format!("Row {} Col 2 ('name') not string", idx))?.to_string();
                
                let entity_id_val: Option<u32> = match &row_array[3] {
                    JsonValue::Null => None,
                    JsonValue::Number(n) => n.as_u64().map(|id| id as u32),
                    _ => return Err(format!("Row {} Col 3 ('entity_id') invalid type", idx)),
                };

                // Warning: Inserting player_id might clash with auto_inc!
                let new_player = Player { identity, player_id: player_id_val, name: name_val, entity_id: entity_id_val };
                ctx.db.player().insert(new_player);
                inserted_count += 1;
             }
        }
         "message" => {
            // 1. Delete existing messages (Requires PK)
            // TODO: Add a primary key (e.g., message_id u64 auto_inc) to Message struct!
            log::warn!("[Reducer] Deletion skipped for 'message' table as it lacks a primary key. New messages will be appended.");
            // If PK existed:
            // let existing_messages: Vec<Message> = ctx.db.message().iter().collect();
            // log::debug!("[Reducer] Deleting {} existing messages...", existing_messages.len());
            // for msg in existing_messages { Message::delete_by_message_id(&ctx.db, &msg.message_id); }

            // 2. Insert new messages
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 4 { return Err(format!("Row {} for '{}' too few cols (exp 4)", idx, table_name)); }

                // --- Corrected Identity Parsing (from nested array) ---
                let sender_inner_array = row_array[0].as_array()
                    .ok_or_else(|| format!("Row {} Col 0 ('sender') not array", idx))?;
                if sender_inner_array.is_empty() {
                    return Err(format!("Row {} Col 0 ('sender') inner array is empty", idx));
                }
                let sender_hex = sender_inner_array[0].as_str()
                    .ok_or_else(|| format!("Row {} Col 0 ('sender')[0] not string", idx))?;
                let sender = Identity::from_hex(sender_hex)
                    .map_err(|_| format!("Row {} Col 0 ('sender') invalid hex: {}", idx, sender_hex))?;
                // --- End Corrected Identity Parsing ---

                // --- Corrected Timestamp Parsing (from nested array) ---
                let sent_inner_array = row_array[1].as_array()
                    .ok_or_else(|| format!("Row {} Col 1 ('sent') not array", idx))?;
                if sent_inner_array.is_empty() {
                    return Err(format!("Row {} Col 1 ('sent') inner array is empty", idx));
                }
                // SpacetimeDB Timestamps use i64 for micros
                let sent_unix_micros = sent_inner_array[0].as_i64() 
                    .ok_or_else(|| format!("Row {} Col 1 ('sent')[0] not i64 number", idx))?;
                let sent = Timestamp::from_micros_since_unix_epoch(sent_unix_micros);
                // --- End Corrected Timestamp Parsing ---

                let text_val = row_array[2].as_str().ok_or(format!("Row {} Col 2 ('text') not string", idx))?.to_string();
                let sender_name_val = row_array[3].as_str().ok_or(format!("Row {} Col 3 ('sender_name') not string", idx))?.to_string();

                let new_message = Message { sender, sent, text: text_val, sender_name: sender_name_val };
                ctx.db.message().insert(new_message);
                inserted_count += 1;
            }
        }
        "messages" => {
            // 1. Delete existing messages
            let existing_messages: Vec<Messages> = ctx.db.messages().iter().collect();
            log::debug!("[Reducer] Deleting {} existing messages entries...", existing_messages.len());
            for msg in existing_messages {
                ctx.db.messages().id().delete(&msg.id);
            }

            // 2. Insert new messages
            for (idx, row_val) in rows_json.iter().enumerate() {
                let row_array = row_val.as_array().ok_or_else(|| format!("Row {} not array", idx))?;
                if row_array.len() < 5 { return Err(format!("Row {} for '{}' too few cols (exp 5)", idx, table_name)); }

                let id_val = row_array[0].as_u64().ok_or(format!("Row {} Col 0 ('id') not u64", idx))?;
                
                // --- Identity Parsing ---
                let sender_inner_array = row_array[1].as_array()
                    .ok_or_else(|| format!("Row {} Col 1 ('sender') not array", idx))?;
                if sender_inner_array.is_empty() {
                    return Err(format!("Row {} Col 1 ('sender') inner array is empty", idx));
                }
                let sender_hex = sender_inner_array[0].as_str()
                    .ok_or_else(|| format!("Row {} Col 1 ('sender')[0] not string", idx))?;
                let sender = Identity::from_hex(sender_hex)
                    .map_err(|_| format!("Row {} Col 1 ('sender') invalid hex: {}", idx, sender_hex))?;
                
                // --- Timestamp Parsing ---
                let sent_inner_array = row_array[2].as_array()
                    .ok_or_else(|| format!("Row {} Col 2 ('sent') not array", idx))?;
                if sent_inner_array.is_empty() {
                    return Err(format!("Row {} Col 2 ('sent') inner array is empty", idx));
                }
                let sent_unix_micros = sent_inner_array[0].as_i64() 
                    .ok_or_else(|| format!("Row {} Col 2 ('sent')[0] not i64 number", idx))?;
                let sent = Timestamp::from_micros_since_unix_epoch(sent_unix_micros);
                
                let text_val = row_array[3].as_str().ok_or(format!("Row {} Col 3 ('text') not string", idx))?.to_string();
                let sender_name_val = row_array[4].as_str().ok_or(format!("Row {} Col 4 ('sender_name') not string", idx))?.to_string();

                let new_message = Messages { 
                    id: id_val, 
                    sender, 
                    sent, 
                    text: text_val, 
                    sender_name: sender_name_val 
                };
                ctx.db.messages().insert(new_message);
                inserted_count += 1;
            }
        }
        _ => {
            return Err(format!("Import via reducer not implemented for table: {}", table_name));
        }
    }

    log::info!("[Reducer] Import successful for table '{}'. Processed {} rows.", table_name, inserted_count);
    Ok(())
}