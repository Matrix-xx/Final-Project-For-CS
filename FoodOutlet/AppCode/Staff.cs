using System.IO;
using System.Text.Json;
using FoodOutlet.Models;
using MySql.Data.MySqlClient;

namespace FoodOutlet.AppCode
{
    public class Staff
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public Staff(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        #region GetAll
        public List<Models.Role> GetAllRoles()
        {
            List<Models.Role> roles = new List<Models.Role>();

            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();

                    using (MySqlCommand cmd = new MySqlCommand("SELECT id, role_name FROM roles ORDER BY id", conn))
                    {
                        using (MySqlDataReader rst = cmd.ExecuteReader())
                        {
                            while (rst.Read())
                            {
                                Models.Role role = new Models.Role();

                                role.id = Convert.ToInt32(rst["id"]);
                                // FIX Problem 1: Use null coalescing operator
                                role.role_name = rst["role_name"]?.ToString() ?? "";

                                roles.Add(role);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ROLE ERROR: " + e.Message);
            }

            return roles;
        }

        public List<Models.Staff> GetAllStaffs()
        {
            List<Models.Staff> staffList = new List<Models.Staff>();
            using (MySqlConnection conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                EnsureResignApprovalColumn(conn);
                string query = @"
                    SELECT r.id, r.registration_name AS name, r.email, r.phone_no, r.address, r.role_id, r.photo,
                           ro.role_name,
                           CASE WHEN rs.registration_id IS NULL THEN 'In Service' ELSE 'Resign' END AS status
                    FROM registrations r
                    LEFT JOIN roles ro ON r.role_id = ro.id
                    LEFT JOIN resigns rs ON r.id = rs.registration_id
                        AND COALESCE(rs.approval_status, 'Pending') != 'Rejected'
                    ORDER BY r.id";
                
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader rst = cmd.ExecuteReader())
                    {
                        while (rst.Read())
                        {
                            // Simply retrieve the photo path from database
                            string photoPath = rst["photo"]?.ToString() ?? "";

                            staffList.Add(new Models.Staff
                            {
                                id = rst["id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["id"]),
                                name = rst["name"]?.ToString() ?? "",
                                email = rst["email"]?.ToString() ?? "",
                                phone_no = rst["phone_no"]?.ToString() ?? "",
                                address = rst["address"]?.ToString() ?? "",
                                role_id = rst["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["role_id"]),
                                photo = photoPath,
                                role_name = rst["role_name"]?.ToString() ?? "",
                                status = rst["status"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            return staffList;
        }

        public List<Models.Category> GetAllCategories()
        {
            var list = new List<Models.Category>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT id, category_name FROM categories ORDER BY id", conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new Models.Category
                        {
                            id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                            category_name = rdr["category_name"]?.ToString() ?? ""
                        });
                    }
                }
            }

            return list;
        }

        private static bool _recipeIngredientsMigrated = false;
        private static readonly object _recipeMigrateLock = new();

        private void EnsureRecipeIngredientsColumn(MySqlConnection conn)
        {
            if (_recipeIngredientsMigrated) return;
            lock (_recipeMigrateLock)
            {
                if (_recipeIngredientsMigrated) return;
                try
                {
                    using var check = new MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.columns " +
                        "WHERE table_schema = DATABASE() AND table_name = 'recipes' AND column_name = 'ingredients'", conn);
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    {
                        using var alter = new MySqlCommand("ALTER TABLE recipes ADD COLUMN ingredients TEXT NULL", conn);
                        alter.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureRecipeIngredientsColumn error: " + ex.Message);
                }
                _recipeIngredientsMigrated = true;
            }
        }
        public List<dynamic> GetAllRecipes()
        {
            var list = new List<dynamic>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                EnsureRecipeIngredientsColumn(conn);
                string sql = @"
            SELECT r.id, r.recipe_name, r.category_id, r.recipe_img, r.description, r.ingredients, r.price, c.category_name
            FROM recipes r
            LEFT JOIN categories c ON r.category_id = c.id
            ORDER BY r.id";
                using (var cmd = new MySqlCommand(sql, conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var imagePath = rdr["recipe_img"]?.ToString() ?? "";
                        imagePath = NormalizeImagePath(imagePath);

                        list.Add(new
                        {
                            id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                            recipe_name = rdr["recipe_name"]?.ToString() ?? "",
                            category_id = rdr["category_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["category_id"]),
                            recipe_img = imagePath,
                            description = rdr["description"]?.ToString() ?? "",
                            ingredients = rdr["ingredients"]?.ToString() ?? "",
                            price = rdr["price"] == DBNull.Value ? 0 : Convert.ToDecimal(rdr["price"]),
                            category_name = rdr["category_name"]?.ToString() ?? ""
                        });
                    }
                }
            }
            return list;
        }

        // GetAllInventories - returns one row per recipe, aggregates any duplicate inventory rows
        public List<dynamic> GetAllInventories()
        {
            var list = new List<dynamic>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                // FIXED SQL SYNTAX: Added spaces after "inventories" and "r.id"
                string sql = @"
SELECT r.id AS recipe_id, r.recipe_name, r.recipe_img, 
       COALESCE(i.stock_qty, 0) AS stock_qty, 
       COALESCE(i.inventory_id, 0) AS inventory_id,
       i.created_at, i.updated_at 
FROM recipes r 
LEFT JOIN (
    SELECT recipe_id, SUM(stock_qty) AS stock_qty, MAX(id) AS inventory_id, 
           MAX(created_at) AS created_at, MAX(updated_at) AS updated_at
    FROM inventories
    GROUP BY recipe_id
) i ON i.recipe_id = r.id
ORDER BY r.recipe_name, r.id";

                using (var cmd = new MySqlCommand(sql, conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var imagePath = rdr["recipe_img"] == DBNull.Value ? "" : rdr["recipe_img"].ToString();
                        imagePath = NormalizeImagePath(imagePath);

                        list.Add(new
                        {
                            inventory_id = rdr["inventory_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["inventory_id"]),
                            recipe_id = rdr["recipe_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["recipe_id"]),
                            recipe_name = rdr["recipe_name"]?.ToString() ?? "",
                            recipe_img = imagePath,
                            stock_qty = rdr["stock_qty"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["stock_qty"]),
                            created_at = rdr["created_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["created_at"]),
                            updated_at = rdr["updated_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["updated_at"])
                        });
                    }
                }
            }
            return list;
        }

        #endregion


        #region Get and Set
        // FIX Problem 5: Change return type to nullable (Models.Role?)
        public Models.Role? GetRoleById(int id)
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT id, role_name FROM roles WHERE id = @id LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            return new Models.Role
                            {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                role_name = rdr["role_name"]?.ToString() ?? ""
                            };
                        }
                    }
                }
            }
            return null;
        }

        public Message SetRole(Models.Role role)
        {
            var msg = new Message();
            if (role == null)
            {
                msg.message = "Invalid role";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();

                    if (role.id > 0)
                    {
                        using (var cmd = new MySqlCommand("UPDATE roles SET role_name = @role_name WHERE id = @id", conn))
                        {
                            cmd.Parameters.AddWithValue("@role_name", role.role_name ?? "");
                            cmd.Parameters.AddWithValue("@id", role.id);
                            cmd.ExecuteNonQuery();
                        }
                        msg.message = "Updated";
                    }
                    else
                    {
                        using (var cmd = new MySqlCommand("INSERT INTO roles (role_name) VALUES (@role_name)", conn))
                        {
                            cmd.Parameters.AddWithValue("@role_name", role.role_name ?? "");
                            cmd.ExecuteNonQuery();
                        }
                        msg.message = "Success";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
            }

            return msg;
        }

        // FIX Problem 10: Change return type to nullable (Models.Staff?)
        // FIXED: Added photo field to retrieval
        public Models.Staff? GetStaffById(int id)
        {
            using (MySqlConnection conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT id,registration_name AS name,email,password_hash,birth_of_date,phone_no,address,role_id,photo FROM registrations WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader rst = cmd.ExecuteReader())
                    {
                        if (rst.Read())
                        {
                            string photoData = "";
                            
                            // Safely retrieve photo data
                            int photoOrdinal = rst.GetOrdinal("photo");
                            if (!rst.IsDBNull(photoOrdinal))
                            {
                                try
                                {
                                    photoData = rst.GetString(photoOrdinal) ?? "";
                                }
                                catch
                                {
                                    // If string conversion fails, try as byte array
                                    try
                                    {
                                        byte[] photoBytes = rst.GetFieldValue<byte[]>(photoOrdinal);
                                        if (photoBytes != null && photoBytes.Length > 0)
                                        {
                                            photoData = Convert.ToBase64String(photoBytes);
                                            photoData = "data:image/jpeg;base64," + photoData;
                                        }
                                    }
                                    catch
                                    {
                                        photoData = "";
                                    }
                                }
                            }

                            return new Models.Staff
                            {
                                id = rst["id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["id"]),
                                name = rst["name"]?.ToString() ?? "",
                                email = rst["email"]?.ToString() ?? "",
                                password = rst["password_hash"]?.ToString() ?? "",
                                birth_of_date = rst["birth_of_date"] == DBNull.Value ? null : Convert.ToDateTime(rst["birth_of_date"]),
                                phone_no = rst["phone_no"]?.ToString() ?? "",
                                address = rst["address"]?.ToString() ?? "",
                                role_id = rst["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["role_id"]),
                                photo = photoData
                            };
                        }
                    }
                }
            }
            return null;
        }

        public Message SetStaff(Models.Staff staf)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    
                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(staf.name))
                    {
                        msg.message = "Error: Staff name is required";
                        return msg;
                    }
                    if (string.IsNullOrWhiteSpace(staf.email))
                    {
                        msg.message = "Error: Email is required";
                        return msg;
                    }
                    if (staf.role_id <= 0)
                    {
                        msg.message = "Error: Role must be selected";
                        return msg;
                    }
                    if (string.IsNullOrWhiteSpace(staf.password) && staf.id == 0)
                    {
                        msg.message = "Error: Password is required for new staff";
                        return msg;
                    }

                    if (staf.id > 0)
                    {
                        // UPDATE existing staff
                        using (MySqlCommand cmd = new MySqlCommand(
                            "UPDATE registrations SET registration_name=@name, email=@email, birth_of_date=@birth_of_date, " +
                            "password_hash=@password_hash, phone_no=@phone_no, address=@address, role_id=@role_id" +
                            (string.IsNullOrEmpty(staf.photo) ? "" : ", photo=@photo") +
                            " WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", staf.id);
                            cmd.Parameters.AddWithValue("@name", staf.name ?? "");
                            cmd.Parameters.AddWithValue("@email", staf.email ?? "");
                            cmd.Parameters.AddWithValue("@birth_of_date", staf.birth_of_date ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@password_hash", string.IsNullOrEmpty(staf.password) ? DBNull.Value : (object)staf.password);
                            cmd.Parameters.AddWithValue("@phone_no", staf.phone_no ?? "");
                            cmd.Parameters.AddWithValue("@address", staf.address ?? "");
                            cmd.Parameters.AddWithValue("@role_id", staf.role_id);
                            if (!string.IsNullOrEmpty(staf.photo))
                            {
                                cmd.Parameters.AddWithValue("@photo", staf.photo);
                            }
                            
                            int rowsAffected = cmd.ExecuteNonQuery();
                            msg.message = rowsAffected > 0 ? "Success" : "Error: Staff not found";
                        }
                    }
                    else
                    {
                        // INSERT new staff
                        using (MySqlCommand cmd = new MySqlCommand(
                            "INSERT INTO registrations (registration_name, email, birth_of_date, password_hash, phone_no, address, role_id, photo, created_at) " +
                            "VALUES (@name, @email, @birth_of_date, @password_hash, @phone_no, @address, @role_id, @photo, NOW())", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", staf.name ?? "");
                            cmd.Parameters.AddWithValue("@email", staf.email ?? "");
                            cmd.Parameters.AddWithValue("@birth_of_date", staf.birth_of_date ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@password_hash", staf.password ?? "");
                            cmd.Parameters.AddWithValue("@phone_no", staf.phone_no ?? "");
                            cmd.Parameters.AddWithValue("@address", staf.address ?? "");
                            cmd.Parameters.AddWithValue("@role_id", staf.role_id);
                            cmd.Parameters.AddWithValue("@photo", staf.photo ?? "");
                            
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        public Message SetCategory(Models.Category cat)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    if (cat.id > 0)
                    {
                        using (var cmd = new MySqlCommand("UPDATE categories SET category_name=@name WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", cat.id);
                            cmd.Parameters.AddWithValue("@name", cat.category_name);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                    else
                    {
                        using (var cmd = new MySqlCommand("INSERT INTO categories (category_name) VALUES (@name)", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", cat.category_name);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        public Message SetRecipe(Recipe r)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureRecipeIngredientsColumn(conn);
                    // #region agent log
                    try
                    {
                        var payload = "{\"sessionId\":\"cff0a8\",\"runId\":\"recipe-ingredients-db-save\",\"hypothesisId\":\"H2\",\"location\":\"AppCode/Staff.cs:SetRecipe\",\"message\":\"saving recipe with ingredients\",\"data\":{\"id\":" + r.id + ",\"ingredientsLen\":" + (r.ingredients ?? "").Length + "},\"timestamp\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
                        File.AppendAllText("debug-cff0a8.log", payload + Environment.NewLine);
                    }
                    catch {}
                    // #endregion
                    if (r.id > 0)
                    {
                        using (var cmd = new MySqlCommand("UPDATE recipes SET recipe_name=@name, category_id=@catid, recipe_img=@img, description=@desc, ingredients=@ingredients, price=@price WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", r.id);
                            cmd.Parameters.AddWithValue("@name", r.recipe_name ?? "");
                            cmd.Parameters.AddWithValue("@catid", r.category_id);
                            cmd.Parameters.AddWithValue("@img", r.recipe_img ?? "");
                            cmd.Parameters.AddWithValue("@desc", r.description ?? "");
                            cmd.Parameters.AddWithValue("@ingredients", r.ingredients ?? "");
                            cmd.Parameters.AddWithValue("@price", r.price);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                    else
                    {
                        using (var cmd = new MySqlCommand("INSERT INTO recipes (recipe_name, category_id, recipe_img, description, ingredients, price, created_at) VALUES (@name, @catid, @img, @desc, @ingredients, @price, NOW())", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", r.recipe_name ?? "");
                            cmd.Parameters.AddWithValue("@catid", r.category_id);
                            cmd.Parameters.AddWithValue("@img", r.recipe_img ?? "");
                            cmd.Parameters.AddWithValue("@desc", r.description ?? "");
                            cmd.Parameters.AddWithValue("@ingredients", r.ingredients ?? "");
                            cmd.Parameters.AddWithValue("@price", r.price);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        // FIX Problem 11: Change return type to nullable (Recipe?)
        public Recipe? GetRecipeById(int id)
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                EnsureRecipeIngredientsColumn(conn);
                string sql = "SELECT id, recipe_name, category_id, recipe_img, description, ingredients, price FROM recipes WHERE id=@id";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            return new Recipe
                            {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                recipe_name = rdr["recipe_name"]?.ToString() ?? "",
                                category_id = rdr["category_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["category_id"]),
                                recipe_img = rdr["recipe_img"]?.ToString() ?? "",
                                description = rdr["description"]?.ToString() ?? "",
                            ingredients = rdr["ingredients"]?.ToString() ?? "",
                            price = rdr["price"] == DBNull.Value ? 0 : Convert.ToDecimal(rdr["price"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        // FIX Problem 12: Change return type to nullable (Models.Inventory?)
        public Models.Inventory? GetInventoryById(int id)
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                string sql = @"SELECT id, recipe_id, stock_qty, created_at, updated_at FROM inventories WHERE id = @id LIMIT 1";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            return new Models.Inventory
                            {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                recipe_id = rdr["recipe_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["recipe_id"]),
                                stock_qty = rdr["stock_qty"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["stock_qty"]),
                                created_at = rdr["created_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["created_at"]),
                                updated_at = rdr["updated_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["updated_at"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        // SetInventory - if inserting (id==0) but an inventory row for the recipe exists, update it instead of inserting duplicate
        public Message SetInventory(Models.Inventory inv)
        {
            var msg = new Message();
            if (inv == null || inv.recipe_id <= 0)
            {
                msg.message = "Invalid payload";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        // EDIT: id > 0 => replace quantity for that inventory row
                        if (inv.id > 0)
                        {
                            using (var cmd = new MySqlCommand("UPDATE inventories SET stock_qty = @qty, recipe_id = @rid, updated_at = NOW() WHERE id = @id", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@qty", inv.stock_qty);
                                cmd.Parameters.AddWithValue("@rid", inv.recipe_id);
                                cmd.Parameters.AddWithValue("@id", inv.id);
                                int affected = cmd.ExecuteNonQuery();
                                tx.Commit();
                                msg.message = affected > 0 ? "Updated" : "Error: Not found";
                                return msg;
                            }
                        }

                        // ADD: id == 0 => add quantity to existing row for recipe_id or insert new
                        using (var cmdFind = new MySqlCommand("SELECT id, stock_qty FROM inventories WHERE recipe_id = @rid LIMIT 1 FOR UPDATE", conn, tx))
                        {
                            cmdFind.Parameters.AddWithValue("@rid", inv.recipe_id);
                            using (var rdr = cmdFind.ExecuteReader())
                            {
                                if (rdr.Read())
                                {
                                    int existingId = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]);
                                    int existingQty = rdr["stock_qty"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["stock_qty"]);
                                    rdr.Close();

                                    int newQty = existingQty + inv.stock_qty;
                                    using (var cmdUpd = new MySqlCommand("UPDATE inventories SET stock_qty = @qty, updated_at = NOW() WHERE id = @id", conn, tx))
                                    {
                                        cmdUpd.Parameters.AddWithValue("@qty", newQty);
                                        cmdUpd.Parameters.AddWithValue("@id", existingId);
                                        cmdUpd.ExecuteNonQuery();
                                    }

                                    tx.Commit();
                                    msg.message = "Updated";
                                    return msg;
                                }
                                else
                                {
                                    // no existing row, close reader then insert
                                    rdr.Close();
                                    using (var cmdIns = new MySqlCommand("INSERT INTO inventories (recipe_id, stock_qty, created_at) VALUES (@rid, @qty, NOW())", conn, tx))
                                    {
                                        cmdIns.Parameters.AddWithValue("@rid", inv.recipe_id);
                                        cmdIns.Parameters.AddWithValue("@qty", inv.stock_qty);
                                        cmdIns.ExecuteNonQuery();
                                    }

                                    tx.Commit();
                                    msg.message = "Success";
                                    return msg;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                return msg;
            }
        }
        #endregion

        #region Count
        public int GetStaffCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM registrations", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public int GetCategoryCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM categories", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public int GetRoleCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM roles", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public int GetResignCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM resigns", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public int GetRecipeCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM recipes", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// Count of paid / closed orders (<c>Done</c>) — dashboard &quot;Table Order History&quot; tile.
        /// </summary>
        public int GetOrderHistoryCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM `Order` WHERE status = 'Done'", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public int GetResignPendingCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                EnsureResignApprovalColumn(conn);
                using (var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM resigns WHERE COALESCE(approval_status, 'Pending') = 'Pending'", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public List<dynamic> GetSalesChartData(int days)
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);
                    const string sql = @"
                        SELECT DATE(o.created_at) AS sale_date,
                               COALESCE(SUM(r.price * od.qty), 0) AS total_income
                        FROM `Order` o
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes r ON r.id = od.recipe_id
                        WHERE o.status = 'Done'
                          AND o.created_at >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                        GROUP BY DATE(o.created_at)
                        ORDER BY sale_date ASC";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@days", days);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                list.Add(new
                                {
                                    sale_date = rdr["sale_date"] == DBNull.Value ? "" : Convert.ToDateTime(rdr["sale_date"]).ToString("yyyy-MM-dd"),
                                    total_income = rdr["total_income"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["total_income"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetSalesChartData error: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Income (MMK) per menu category from completed (<c>Done</c>) orders in the last <paramref name="days"/> days — dashboard pie chart.
        /// </summary>
        public List<dynamic> GetCategoryIncomeForDashboard(int days)
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT COALESCE(MAX(c.category_name), 'Uncategorized') AS category_name,
                               SUM(r.price * od.qty) AS total_income
                        FROM `Order` o
                        INNER JOIN order_detail od ON od.order_id = o.id
                        INNER JOIN recipes r ON r.id = od.recipe_id
                        LEFT JOIN categories c ON c.id = r.category_id
                        WHERE o.status = 'Done'
                          AND o.created_at >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                        GROUP BY COALESCE(r.category_id, 0), COALESCE(c.category_name, 'Uncategorized')
                        HAVING SUM(r.price * od.qty) > 0
                        ORDER BY total_income DESC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@days", days);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                list.Add(new
                                {
                                    category_name = rdr["category_name"]?.ToString() ?? "Uncategorized",
                                    total_income = rdr["total_income"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["total_income"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetCategoryIncomeForDashboard error: " + ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Best-selling recipes by units sold among completed (<c>Done</c>) orders in the last <paramref name="days"/> days.
        /// </summary>
        public List<dynamic> GetTopSellingItems(int days, int limit = 5)
        {
            var list = new List<dynamic>();
            if (limit < 1) limit = 1;
            if (limit > 50) limit = 50;

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    string sql = @"
                        SELECT r.recipe_name AS item_name,
                               SUM(od.qty) AS qty_sold,
                               SUM(r.price * od.qty) AS total_income
                        FROM `Order` o
                        INNER JOIN order_detail od ON od.order_id = o.id
                        INNER JOIN recipes r ON r.id = od.recipe_id
                        WHERE o.status = 'Done'
                          AND o.created_at >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                        GROUP BY r.id, r.recipe_name
                        HAVING SUM(od.qty) > 0
                        ORDER BY qty_sold DESC
                        LIMIT " + limit.ToString();

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@days", days);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                list.Add(new
                                {
                                    item_name = rdr["item_name"]?.ToString() ?? "",
                                    qty_sold = rdr["qty_sold"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["qty_sold"]),
                                    total_income = rdr["total_income"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["total_income"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTopSellingItems error: " + ex.Message);
            }

            return list;
        }

        #endregion

        #region Update and Delete

        public Message UpdateStaff(Models.Staff staf)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("UPDATE registrations SET registration_name=@registration_name,birth_of_date=@birth_of_date,role_id=@role_id WHERE id=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@registration_name", staf.name);
                        cmd.Parameters.AddWithValue("@birth_of_date", staf.birth_of_date);
                        cmd.Parameters.AddWithValue("@role_id", staf.role_id);
                        cmd.Parameters.AddWithValue("@id", staf.id);
                        cmd.ExecuteNonQuery();
                        msg.message = "Success";
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        public Message DeleteStaff(int id)
        {
            var msg = new Message();
            if (id <= 0)
            {
                msg.message = "Error: Invalid staff id.";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var delResign = new MySqlCommand("DELETE FROM resigns WHERE registration_id = @id", conn, tx))
                            {
                                delResign.Parameters.AddWithValue("@id", id);
                                delResign.ExecuteNonQuery();
                            }

                            using (var delReg = new MySqlCommand("DELETE FROM registrations WHERE id = @id", conn, tx))
                            {
                                delReg.Parameters.AddWithValue("@id", id);
                                int n = delReg.ExecuteNonQuery();
                                if (n == 0)
                                {
                                    tx.Rollback();
                                    msg.message = "Error: Staff member not found.";
                                    return msg;
                                }
                            }

                            tx.Commit();
                            msg.message = "Success";
                        }
                        catch
                        {
                            try { tx.Rollback(); } catch { /* ignore */ }
                            throw;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
                Console.WriteLine("DeleteStaff error: " + e.Message);
            }

            return msg;
        }

        public Message DeleteRole(int id)
        {
            var msg = new Message();

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("DELETE FROM roles WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
                msg.message = "Success";
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
            }

            return msg;
        }

        public Message DeleteCategory(int id)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("DELETE FROM categories WHERE id=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                        msg.message = "Success";
                    }
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        public Message DeleteRecipe(int id)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmdInv = new MySqlCommand("DELETE FROM inventories WHERE recipe_id = @rid", conn, tx))
                        {
                            cmdInv.Parameters.AddWithValue("@rid", id);
                            cmdInv.ExecuteNonQuery();
                        }

                        using (var cmd = new MySqlCommand("DELETE FROM recipes WHERE id=@id", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }

                    msg.message = "Success";
                }
            }
            catch (Exception e)
            {
                msg.message = "Error: " + e.Message;
            }
            return msg;
        }

        /// <summary>Set inventory quantity to zero (stock cleared). Does not remove the inventory row.</summary>
        public Message DeleteInventory(int id)
        {
            var msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(
                               "UPDATE inventories SET stock_qty = 0, updated_at = NOW() WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int n = cmd.ExecuteNonQuery();
                        msg.message = n > 0 ? "Success" : "Error: Inventory row not found.";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
            }
            return msg;
        }

        #endregion

        /// <summary>
        /// Normalizes recipe image path to ensure correct format: /uploads/recipes/{filename}
        /// Handles legacy paths and ensures consistency
        /// </summary>
        private string NormalizeImagePath(string? imagePath)  // ← Changed to string? (nullable)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return "";

            // Remove leading/trailing whitespace
            imagePath = imagePath.Trim();

            // Already in correct format
            if (imagePath.StartsWith("/uploads/recipes/"))
                return imagePath;

            // Fix legacy path format: /img/recipes/ → /uploads/recipes/
            if (imagePath.StartsWith("/img/recipes/"))
                return imagePath.Replace("/img/recipes/", "/uploads/recipes/");

            // If it's just a filename, prepend the correct path
            if (!imagePath.Contains("/"))
                return $"/uploads/recipes/{imagePath}";

            // Default: return as-is (might be a full URL or already correct)
            return imagePath;
        }

        #region Order Management

        /// <summary>
        /// FK on Order / order_detail references table_lists.id.
        /// Maps a registered table_number to a table_lists row.
        /// </summary>
        private int ResolveTableListIdForOrder(int tableNumber, MySqlConnection conn)
        {
            EnsureUnifiedTableLists(conn);

            using (var byNum = new MySqlCommand("SELECT id FROM table_lists WHERE table_number = @tn LIMIT 1", conn))
            {
                byNum.Parameters.AddWithValue("@tn", tableNumber);
                var o = byNum.ExecuteScalar();
                if (o != null)
                    return Convert.ToInt32(o);
            }

            return 0;
        }

        private static bool _unifiedTableListsMigrated = false;
        private static readonly object _unifiedTableListsLock = new();

        /// <summary>
        /// Merges legacy <c>tables</c> into <c>table_lists</c> (table_number, qr_code) once per app lifetime.
        /// </summary>
        private void EnsureUnifiedTableLists(MySqlConnection conn)
        {
            if (_unifiedTableListsMigrated) return;
            lock (_unifiedTableListsLock)
            {
                if (_unifiedTableListsMigrated) return;
                try
                {
                    using (var colNum = new MySqlCommand(
                               "SELECT COUNT(*) FROM information_schema.columns " +
                               "WHERE table_schema = DATABASE() AND table_name = 'table_lists' AND column_name = 'table_number'", conn))
                    {
                        if (Convert.ToInt32(colNum.ExecuteScalar()) == 0)
                        {
                            using var alter = new MySqlCommand(
                                "ALTER TABLE table_lists ADD COLUMN table_number INT NULL UNIQUE AFTER id", conn);
                            alter.ExecuteNonQuery();
                        }
                    }

                    using (var colQr = new MySqlCommand(
                               "SELECT COUNT(*) FROM information_schema.columns " +
                               "WHERE table_schema = DATABASE() AND table_name = 'table_lists' AND column_name = 'qr_code'", conn))
                    {
                        if (Convert.ToInt32(colQr.ExecuteScalar()) == 0)
                        {
                            using var alter = new MySqlCommand(
                                "ALTER TABLE table_lists ADD COLUMN qr_code VARCHAR(512) NULL AFTER table_name", conn);
                            alter.ExecuteNonQuery();
                        }
                    }

                    using (var legacy = new MySqlCommand(
                               "SELECT COUNT(*) FROM information_schema.tables " +
                               "WHERE table_schema = DATABASE() AND table_name = 'tables'", conn))
                    {
                        if (Convert.ToInt32(legacy.ExecuteScalar()) > 0)
                        {
                            using var upd = new MySqlCommand(
                                "UPDATE table_lists tl " +
                                "INNER JOIN `tables` t ON (" +
                                "  tl.table_name = CAST(t.table_number AS CHAR) " +
                                "  OR tl.table_name = CONCAT('Table ', t.table_number)" +
                                ") " +
                                "SET tl.table_number = t.table_number, " +
                                "    tl.qr_code = COALESCE(NULLIF(tl.qr_code, ''), t.qr_code) " +
                                "WHERE tl.table_number IS NULL", conn);
                            upd.ExecuteNonQuery();

                            using var ins = new MySqlCommand(
                                "INSERT INTO table_lists (table_number, table_name, qr_code, is_available, created_at) " +
                                "SELECT t.table_number, CAST(t.table_number AS CHAR), t.qr_code, 1, t.created_at " +
                                "FROM `tables` t " +
                                "WHERE NOT EXISTS (" +
                                "  SELECT 1 FROM table_lists tl WHERE tl.table_number = t.table_number" +
                                ")", conn);
                            ins.ExecuteNonQuery();
                        }
                    }

                    using (var backfill = new MySqlCommand(
                               "UPDATE table_lists " +
                               "SET table_number = CAST(TRIM(table_name) AS UNSIGNED) " +
                               "WHERE table_number IS NULL " +
                               "  AND TRIM(IFNULL(table_name,'')) REGEXP '^[0-9]+$'", conn))
                    {
                        backfill.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureUnifiedTableLists error: " + ex.Message);
                }

                _unifiedTableListsMigrated = true;
            }
        }

        private static bool _statusTableMigrated = false;
        private static readonly object _statusTableLock = new();

        /// <summary>
        /// Creates the <c>status</c> lookup table when missing (required by order_detail.status_id).
        /// </summary>
        private void EnsureStatusTable(MySqlConnection conn)
        {
            if (_statusTableMigrated) return;
            lock (_statusTableLock)
            {
                if (_statusTableMigrated) return;
                try
                {
                    using var check = new MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.tables " +
                        "WHERE table_schema = DATABASE() AND table_name = 'status'", conn);
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    {
                        using var create = new MySqlCommand(
                            "CREATE TABLE `status` (" +
                            "id INT AUTO_INCREMENT PRIMARY KEY, " +
                            "name VARCHAR(50) NOT NULL" +
                            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);
                        create.ExecuteNonQuery();

                        using var seed = new MySqlCommand(
                            "INSERT INTO `status` (id, name) VALUES " +
                            "(1, 'Pending'), (2, 'Preparing'), (3, 'Served'), " +
                            "(4, 'Done'), (5, 'Cleaning'), (6, 'Cancelled')", conn);
                        seed.ExecuteNonQuery();
                        Console.WriteLine("status table created and seeded.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureStatusTable error: " + ex.Message);
                }

                _statusTableMigrated = true;
            }
        }

        private int ResolveInitialStatusId(MySqlConnection conn)
        {
            EnsureStatusTable(conn);

            using (var byId = new MySqlCommand("SELECT id FROM `status` WHERE id = 1 LIMIT 1", conn))
            {
                var existing = byId.ExecuteScalar();
                if (existing != null)
                    return 1;
            }

            using (var byName = new MySqlCommand("SELECT id FROM `status` WHERE LOWER(name) IN ('pending','new','ordered') ORDER BY id ASC LIMIT 1", conn))
            {
                var named = byName.ExecuteScalar();
                if (named != null)
                {
                    return Convert.ToInt32(named);
                }
            }

            using (var ins = new MySqlCommand("INSERT INTO `status` (name) VALUES ('Pending')", conn))
            {
                ins.ExecuteNonQuery();
            }
            using (var lid = new MySqlCommand("SELECT LAST_INSERT_ID()", conn))
            {
                return Convert.ToInt32(lid.ExecuteScalar());
            }
        }

        /// <summary>
        /// Ensures <c>table_lists</c> has merged columns and legacy <c>tables</c> data is migrated.
        /// Call before any direct query on table_lists from controllers.
        /// </summary>
        public void EnsureTableListsSchema()
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    EnsureTableListsHasAvailability(conn);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EnsureTableListsSchema: " + ex.Message);
            }
        }

        public List<Models.Table> GetAllRegisteredTables()
        {
            var list = new List<Models.Table>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    using (var cmd = new MySqlCommand(
                               "SELECT id, table_number, qr_code, created_at FROM table_lists " +
                               "WHERE table_number IS NOT NULL ORDER BY table_number ASC", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new Models.Table
                            {
                                id = Convert.ToInt32(rdr["id"]),
                                table_number = Convert.ToInt32(rdr["table_number"]),
                                qr_code = rdr["qr_code"]?.ToString() ?? "",
                                created_at = Convert.ToDateTime(rdr["created_at"])
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetAllRegisteredTables error: " + ex.Message);
            }

            return list;
        }

        public bool RegisteredTableNumberExists(int tableNumber)
        {
            if (tableNumber <= 0) return false;
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    using (var cmd = new MySqlCommand(
                               "SELECT COUNT(*) FROM table_lists WHERE table_number = @tn", conn))
                    {
                        cmd.Parameters.AddWithValue("@tn", tableNumber);
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("RegisteredTableNumberExists error: " + ex.Message);
                return false;
            }
        }

        public string? GetRegisteredTableQrPath(int id)
        {
            if (id <= 0) return null;
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    using (var cmd = new MySqlCommand(
                               "SELECT qr_code FROM table_lists WHERE id = @id LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        return cmd.ExecuteScalar()?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetRegisteredTableQrPath error: " + ex.Message);
                return null;
            }
        }

        public Message InsertRegisteredTable(int tableNumber, string qrCodeRelativePath)
        {
            var msg = new Message();
            if (tableNumber <= 0)
            {
                msg.message = "Error: Invalid table number.";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    EnsureTableListsHasAvailability(conn);
                    using (var cmd = new MySqlCommand(
                               "INSERT INTO table_lists (table_number, table_name, qr_code, is_available, created_at) " +
                               "VALUES (@tn, @name, @qr, 0, NOW())", conn))
                    {
                        cmd.Parameters.AddWithValue("@tn", tableNumber);
                        cmd.Parameters.AddWithValue("@name", tableNumber.ToString());
                        cmd.Parameters.AddWithValue("@qr", qrCodeRelativePath);
                        cmd.ExecuteNonQuery();
                    }
                }

                msg.message = "Success";
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("InsertRegisteredTable error: " + ex.Message);
            }

            return msg;
        }

        public Message DeleteRegisteredTable(int id)
        {
            var msg = new Message();
            if (id <= 0)
            {
                msg.message = "Error: Invalid table.";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    using (var cmd = new MySqlCommand("DELETE FROM table_lists WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int n = cmd.ExecuteNonQuery();
                        msg.message = n > 0 ? "Success" : "Error: Table not found.";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("DeleteRegisteredTable error: " + ex.Message);
            }

            return msg;
        }

        /// <summary>
        /// Kept for backward compatibility; runs schema migration only.
        /// </summary>
        public void EnsureTableListRowForRegisteredTable(int tableNumber)
        {
            EnsureTableListsSchema();
        }

        /// <summary>
        /// Create order with items
        /// </summary>
        // Tracks whether the order_id column migration has run this app-lifetime.
        private static bool _orderDetailMigrated = false;
        private static readonly object _migrateLock = new();

        private static bool _tableListsAvailMigrated = false;
        private static readonly object _tableListsAvailLock = new();

        /// <summary>
        /// Adds <c>is_available</c> to <c>table_lists</c> for QR open/closed and cashier override.
        /// </summary>
        private void EnsureTableListsHasAvailability(MySqlConnection conn)
        {
            EnsureUnifiedTableLists(conn);
            if (_tableListsAvailMigrated) return;
            lock (_tableListsAvailLock)
            {
                if (_tableListsAvailMigrated) return;
                try
                {
                    using var check = new MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.columns " +
                        "WHERE table_schema = DATABASE() AND table_name = 'table_lists' AND column_name = 'is_available'", conn);
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    {
                        using var alter = new MySqlCommand(
                            "ALTER TABLE table_lists ADD COLUMN is_available TINYINT(1) NOT NULL DEFAULT 1", conn);
                        alter.ExecuteNonQuery();
                        Console.WriteLine("table_lists.is_available column added.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureTableListsHasAvailability error: " + ex.Message);
                }

                _tableListsAvailMigrated = true;
            }
        }

        /// <summary>
        /// After cleaner marks orders <c>Done</c>, table becomes available for new QR guests;
        /// after cashier sends to <c>Cleaning</c>, table is marked unavailable until cleaned.
        /// </summary>
        private void SyncTableAvailabilityForOrders(MySqlConnection conn, List<int> orderIds, string newStatus)
        {
            if (newStatus != "Cleaning" && newStatus != "Done") return;
            EnsureTableListsHasAvailability(conn);
            if (orderIds == null || orderIds.Count == 0) return;

            var distinctOrderIds = orderIds.Where(id => id > 0).Distinct().ToList();
            if (distinctOrderIds.Count == 0) return;

            var ph = string.Join(",", distinctOrderIds.Select((_, i) => $"@oid{i}"));
            var sql = $"SELECT DISTINCT table_id FROM `Order` WHERE id IN ({ph})";
            var tableIds = new List<int>();
            using (var cmd = new MySqlCommand(sql, conn))
            {
                for (int i = 0; i < distinctOrderIds.Count; i++)
                    cmd.Parameters.AddWithValue($"@oid{i}", distinctOrderIds[i]);
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        if (rdr["table_id"] != DBNull.Value)
                            tableIds.Add(Convert.ToInt32(rdr["table_id"]));
                    }
                }
            }

            tableIds = tableIds.Distinct().ToList();
            if (tableIds.Count == 0) return;

            int available = newStatus == "Done" ? 1 : 0;
            var tp = string.Join(",", tableIds.Select((_, i) => $"@tid{i}"));
            using (var up = new MySqlCommand($"UPDATE table_lists SET is_available = @av WHERE id IN ({tp})", conn))
            {
                up.Parameters.AddWithValue("@av", available);
                for (int i = 0; i < tableIds.Count; i++)
                    up.Parameters.AddWithValue($"@tid{i}", tableIds[i]);
                up.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Adds order_id column to order_detail if it doesn't exist yet.
        /// Must be called OUTSIDE any active transaction (ALTER TABLE causes implicit commit).
        /// </summary>
        private void EnsureOrderDetailOrderId(MySqlConnection conn)
        {
            if (_orderDetailMigrated) return;
            lock (_migrateLock)
            {
                if (_orderDetailMigrated) return;
                try
                {
                    using var check = new MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.columns " +
                        "WHERE table_schema = DATABASE() AND table_name = 'order_detail' AND column_name = 'order_id'", conn);
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    {
                        using var alter = new MySqlCommand(
                            "ALTER TABLE order_detail ADD COLUMN order_id INT NULL DEFAULT NULL", conn);
                        alter.ExecuteNonQuery();
                        Console.WriteLine("order_detail.order_id column added.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureOrderDetailOrderId error: " + ex.Message);
                }
                _orderDetailMigrated = true;
            }
        }

        public Message CreateOrder(int tableNumber, List<Models.OrderItem> items)
        {
            var msg = new Message();
            if (items == null || items.Count == 0)
            {
                msg.message = "No items to order";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn); // must run before transaction
                    EnsureTableListsHasAvailability(conn);
                    EnsureStatusTable(conn);
                    using (var tx = conn.BeginTransaction())
                    {
                        int tableId = ResolveTableListIdForOrder(tableNumber, conn);
                        if (tableId == 0)
                        {
                            msg.message = "Invalid table number";
                            tx.Rollback();
                            return msg;
                        }

                        using (var availChk = new MySqlCommand(
                                   "SELECT COALESCE(is_available, 1) FROM table_lists WHERE id = @tid FOR UPDATE", conn, tx))
                        {
                            availChk.Parameters.AddWithValue("@tid", tableId);
                            var av = availChk.ExecuteScalar();
                            if (av == null || Convert.ToInt32(av) == 0)
                            {
                                msg.message = "This table is not available for ordering right now. Please ask staff.";
                                tx.Rollback();
                                return msg;
                            }
                        }

                        var statusId = ResolveInitialStatusId(conn);

                        // Merge duplicated recipe lines in one order request to validate/deduct correctly.
                        var requiredByRecipe = new Dictionary<int, int>();
                        foreach (var item in items)
                        {
                            if (item == null || item.recipe_id <= 0 || item.qty <= 0)
                            {
                                msg.message = "Invalid order item";
                                tx.Rollback();
                                return msg;
                            }

                            if (!requiredByRecipe.ContainsKey(item.recipe_id))
                                requiredByRecipe[item.recipe_id] = 0;
                            requiredByRecipe[item.recipe_id] += item.qty;
                        }

                        // Validate stock with row locks and stage deductions.
                        var deductions = new Dictionary<int, List<(int inventoryId, int deductQty)>>();
                        foreach (var req in requiredByRecipe)
                        {
                            int recipeId = req.Key;
                            int neededQty = req.Value;

                            var rows = new List<(int inventoryId, int stockQty)>();
                            using (var cmd = new MySqlCommand("SELECT id, stock_qty FROM inventories WHERE recipe_id = @rid FOR UPDATE", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@rid", recipeId);
                                using (var rdr = cmd.ExecuteReader())
                                {
                                    while (rdr.Read())
                                    {
                                        rows.Add((
                                            Convert.ToInt32(rdr["id"]),
                                            rdr["stock_qty"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["stock_qty"])
                                        ));
                                    }
                                }
                            }

                            int totalStock = rows.Sum(r => r.stockQty);
                            if (totalStock < neededQty)
                            {
                                string recipeName = recipeId.ToString();
                                try
                                {
                                    using (var nameCmd = new MySqlCommand("SELECT recipe_name FROM recipes WHERE id = @rid LIMIT 1", conn, tx))
                                    {
                                        nameCmd.Parameters.AddWithValue("@rid", recipeId);
                                        var n = nameCmd.ExecuteScalar();
                                        if (n != null) recipeName = n.ToString()!;
                                    }
                                }
                                catch { }
                                msg.message = $"Not enough stock for \"{recipeName}\". Available: {totalStock}, Requested: {neededQty}";
                                tx.Rollback();
                                return msg;
                            }

                            int remaining = neededQty;
                            var plan = new List<(int inventoryId, int deductQty)>();
                            foreach (var row in rows)
                            {
                                if (remaining <= 0) break;
                                if (row.stockQty <= 0) continue;
                                int take = Math.Min(row.stockQty, remaining);
                                plan.Add((row.inventoryId, take));
                                remaining -= take;
                            }
                            deductions[recipeId] = plan;
                        }

                        // Create order record after stock validation passes.
                        using (var cmd = new MySqlCommand("INSERT INTO `Order` (table_id, status) VALUES (@tid, 'Pending')", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@tid", tableId);
                            cmd.ExecuteNonQuery();
                        }

                        // Keep for future relation/reporting if needed.
                        int orderId = 0;
                        using (var cmd = new MySqlCommand("SELECT LAST_INSERT_ID()", conn, tx))
                        {
                            orderId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // Insert each line item — stamp order_id so items are tied to this order only.
                        foreach (var item in items)
                        {
                            using (var cmd = new MySqlCommand(
                                "INSERT INTO order_detail (table_id, recipe_id, qty, status_id, order_id) " +
                                "VALUES (@tid, @rid, @qty, @sid, @oid)", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@tid", tableId);
                                cmd.Parameters.AddWithValue("@rid", item.recipe_id);
                                cmd.Parameters.AddWithValue("@qty", item.qty);
                                cmd.Parameters.AddWithValue("@sid", statusId);
                                cmd.Parameters.AddWithValue("@oid", orderId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Apply stock deductions.
                        foreach (var recipePlan in deductions)
                        {
                            foreach (var step in recipePlan.Value)
                            {
                                using (var cmd = new MySqlCommand("UPDATE inventories SET stock_qty = stock_qty - @dq, updated_at = NOW() WHERE id = @id", conn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@dq", step.deductQty);
                                    cmd.Parameters.AddWithValue("@id", step.inventoryId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        tx.Commit();
                        msg.message = "Success";
                        return msg;
                    } // transaction scope
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("Order creation error: " + ex.Message);
            }

            return msg;
        }

        #endregion

        #region Order Workflow

        /// <summary>
        /// Returns orders (with item summaries) whose status matches one of the supplied values.
        /// Joins table_lists for the table label and order_detail + recipes for items.
        /// NOTE: order_detail is linked by table_id (not order_id) so this works correctly
        /// when one active order exists per table at a time — normal restaurant operation.
        /// </summary>
        public List<dynamic> GetOrdersWithItems(params string[] statuses)
        {
            var list = new List<dynamic>();
            if (statuses == null || statuses.Length == 0) return list;

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn); // ensure column exists before querying

                    var paramNames = statuses.Select((_, i) => $"@s{i}").ToArray();
                    string inClause = string.Join(", ", paramNames);

                    string sql = $@"
                        SELECT
                            o.id          AS order_id,
                            o.status,
                            o.created_at,
                            tl.table_name AS table_label,
                            GROUP_CONCAT(
                                CONCAT(r.recipe_name, ' x', od.qty)
                                ORDER BY od.id
                                SEPARATOR ', '
                            ) AS items_summary
                        FROM `Order` o
                        JOIN  table_lists  tl ON tl.id    = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes       r  ON r.id       = od.recipe_id
                        WHERE o.status IN ({inClause})
                        GROUP BY o.id, o.status, o.created_at, tl.table_name
                        ORDER BY o.created_at ASC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        for (int i = 0; i < statuses.Length; i++)
                            cmd.Parameters.AddWithValue(paramNames[i], statuses[i]);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var summary = rdr["items_summary"]?.ToString() ?? "—";
                                list.Add(new
                                {
                                    order_id      = Convert.ToInt32(rdr["order_id"]),
                                    status        = rdr["status"]?.ToString() ?? "",
                                    created_at    = rdr["created_at"] == DBNull.Value
                                                        ? DateTime.MinValue
                                                        : Convert.ToDateTime(rdr["created_at"]),
                                    table_label   = rdr["table_label"]?.ToString() ?? "?",
                                    items_summary = summary,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetOrdersWithItems error: " + ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Admin order list: every order with line totals and item summary.
        /// </summary>
        public List<dynamic> GetOrderHistoryAll()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT o.id AS order_id,
                               o.status,
                               o.created_at,
                               tl.table_name AS table_label,
                               COALESCE(SUM(r.price * od.qty), 0) AS order_total,
                               GROUP_CONCAT(
                                   CONCAT(r.recipe_name, ' x', od.qty)
                                   ORDER BY od.id
                                   SEPARATOR ', '
                               ) AS items_summary
                        FROM `Order` o
                        JOIN table_lists tl ON tl.id = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes r ON r.id = od.recipe_id
                        GROUP BY o.id, o.status, o.created_at, tl.table_name
                        ORDER BY o.created_at DESC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                order_id = rdr["order_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["order_id"]),
                                status = rdr["status"]?.ToString() ?? "",
                                created_at = rdr["created_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["created_at"]),
                                table_label = rdr["table_label"]?.ToString() ?? "?",
                                order_total = rdr["order_total"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["order_total"]),
                                items_summary = rdr["items_summary"]?.ToString() ?? "—",
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetOrderHistoryAll error: " + ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Paid / completed orders only (<c>Done</c> workflow status — matches dashboard &quot;Paid&quot;).
        /// </summary>
        public List<dynamic> GetOrderHistoryPaid()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT o.id AS order_id,
                               o.status,
                               o.created_at,
                               tl.table_name AS table_label,
                               COALESCE(SUM(r.price * od.qty), 0) AS order_total,
                               GROUP_CONCAT(
                                   CONCAT(r.recipe_name, ' x', od.qty)
                                   ORDER BY od.id
                                   SEPARATOR ', '
                               ) AS items_summary
                        FROM `Order` o
                        JOIN table_lists tl ON tl.id = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes r ON r.id = od.recipe_id
                        WHERE o.status = 'Done'
                        GROUP BY o.id, o.status, o.created_at, tl.table_name
                        ORDER BY o.created_at DESC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                order_id = rdr["order_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["order_id"]),
                                status = rdr["status"]?.ToString() ?? "",
                                created_at = rdr["created_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["created_at"]),
                                table_label = rdr["table_label"]?.ToString() ?? "?",
                                order_total = rdr["order_total"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["order_total"]),
                                items_summary = rdr["items_summary"]?.ToString() ?? "—",
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetOrderHistoryPaid error: " + ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Full line breakdown for one paid/completed (<c>Done</c>) order — Table Order History detail.
        /// </summary>
        public dynamic? GetTableOrderPaidDetail(int orderId)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    int oid = 0;
                    string status = "";
                    DateTime? createdAt = null;
                    string tableLabel = "?";

                    using (var cmd = new MySqlCommand(
                        @"SELECT o.id, o.status, o.created_at, tl.table_name
                          FROM `Order` o
                          JOIN table_lists tl ON tl.id = o.table_id
                          WHERE o.id = @id AND o.status = 'Done'
                          LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", orderId);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (!rdr.Read()) return null;
                            oid = Convert.ToInt32(rdr["id"]);
                            status = rdr["status"]?.ToString() ?? "";
                            createdAt = rdr["created_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["created_at"]);
                            tableLabel = rdr["table_name"]?.ToString() ?? "?";
                        }
                    }

                    var items = new List<dynamic>();
                    decimal total = 0m;
                    using (var cmd = new MySqlCommand(
                        @"SELECT r.recipe_name, od.qty, r.price
                          FROM order_detail od
                          LEFT JOIN recipes r ON r.id = od.recipe_id
                          WHERE od.order_id = @id
                          ORDER BY od.id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", orderId);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                int qty = rdr["qty"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["qty"]);
                                decimal price = rdr["price"] == DBNull.Value ? 0m : Convert.ToDecimal(rdr["price"]);
                                decimal line = price * qty;
                                total += line;
                                items.Add(new
                                {
                                    recipe_name = rdr["recipe_name"]?.ToString() ?? "",
                                    qty,
                                    price,
                                    line_total = line
                                });
                            }
                        }
                    }

                    return new
                    {
                        order_id = oid,
                        status,
                        created_at = createdAt,
                        table_label = tableLabel,
                        items,
                        order_total = total
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTableOrderPaidDetail error: " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Returns all active orders for a given table (Pending / Approved / Ready / Served).
        /// Excludes Cleaning, Done, and Cancelled so the list resets once the cashier sends
        /// the table to clean.
        /// </summary>
        public List<dynamic> GetTableOrderHistory(int tableNumber)
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT
                            o.id                               AS order_id,
                            o.status,
                            o.created_at,
                            COALESCE(SUM(r.price * od.qty), 0) AS order_total,
                            GROUP_CONCAT(
                                CONCAT(r.recipe_name, ' x', od.qty)
                                ORDER BY od.id
                                SEPARATOR ', '
                            )                                  AS items_summary
                        FROM `Order` o
                        JOIN  table_lists  tl ON tl.id       = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes       r  ON r.id       = od.recipe_id
                        WHERE tl.table_name IN (@tnStr, @tnFmt)
                          AND o.status NOT IN ('Cleaning', 'Done', 'Cancelled')
                        GROUP BY o.id, o.status, o.created_at
                        ORDER BY o.created_at ASC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@tnStr", tableNumber.ToString());
                        cmd.Parameters.AddWithValue("@tnFmt", $"Table {tableNumber}");

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                list.Add(new
                                {
                                    order_id      = Convert.ToInt32(rdr["order_id"]),
                                    status        = rdr["status"]?.ToString() ?? "",
                                    created_at    = rdr["created_at"] == DBNull.Value
                                                        ? DateTime.MinValue
                                                        : Convert.ToDateTime(rdr["created_at"]),
                                    order_total   = rdr["order_total"] == DBNull.Value
                                                        ? 0m
                                                        : Convert.ToDecimal(rdr["order_total"]),
                                    items_summary = rdr["items_summary"]?.ToString() ?? "—",
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTableOrderHistory error: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Groups all currently-Cleaning orders by table (same structure as GetServedByTable).
        /// The cleaner marks the whole table Done in one click.
        /// </summary>
        public List<dynamic> GetCleaningByTable()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT
                            tl.table_name                                                              AS table_label,
                            GROUP_CONCAT(DISTINCT CAST(o.id AS CHAR) ORDER BY o.id SEPARATOR ',')      AS order_ids,
                            MIN(o.created_at)                                                          AS first_ordered_at,
                            GROUP_CONCAT(
                                CONCAT(r.recipe_name, ' x', od.qty)
                                ORDER BY od.id
                                SEPARATOR '|'
                            )                                                                          AS items_raw
                        FROM `Order` o
                        JOIN  table_lists  tl ON tl.id       = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes       r  ON r.id       = od.recipe_id
                        WHERE o.status = 'Cleaning'
                        GROUP BY tl.table_name, o.table_id
                        ORDER BY MIN(o.created_at) ASC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                table_label      = rdr["table_label"]?.ToString() ?? "?",
                                order_ids        = rdr["order_ids"]?.ToString() ?? "",
                                first_ordered_at = rdr["first_ordered_at"] == DBNull.Value
                                                       ? DateTime.MinValue
                                                       : Convert.ToDateTime(rdr["first_ordered_at"]),
                                items_raw        = rdr["items_raw"]?.ToString() ?? "—",
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetCleaningByTable error: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Groups all currently-Served orders by table. Each entry contains the table label,
        /// a comma-separated list of order IDs, the earliest order time, an aggregated items
        /// summary, and the total cost (sum of recipe price × qty across all orders for that
        /// table).
        /// </summary>
        public List<dynamic> GetServedByTable()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureOrderDetailOrderId(conn);

                    const string sql = @"
                        SELECT
                            tl.table_name                                                              AS table_label,
                            GROUP_CONCAT(DISTINCT CAST(o.id AS CHAR) ORDER BY o.id SEPARATOR ',')      AS order_ids,
                            MIN(o.created_at)                                                          AS first_ordered_at,
                            COALESCE(SUM(r.price * od.qty), 0)                                         AS total_cost,
                            GROUP_CONCAT(
                                CONCAT(r.recipe_name, ' x', od.qty)
                                ORDER BY od.id
                                SEPARATOR '|'
                            )                                                                          AS items_raw
                        FROM `Order` o
                        JOIN  table_lists  tl ON tl.id       = o.table_id
                        LEFT JOIN order_detail od ON od.order_id = o.id
                        LEFT JOIN recipes       r  ON r.id       = od.recipe_id
                        WHERE o.status = 'Served'
                        GROUP BY tl.table_name, o.table_id
                        ORDER BY MIN(o.created_at) ASC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                table_label      = rdr["table_label"]?.ToString() ?? "?",
                                order_ids        = rdr["order_ids"]?.ToString() ?? "",
                                first_ordered_at = rdr["first_ordered_at"] == DBNull.Value
                                                       ? DateTime.MinValue
                                                       : Convert.ToDateTime(rdr["first_ordered_at"]),
                                total_cost       = rdr["total_cost"] == DBNull.Value
                                                       ? 0m
                                                       : Convert.ToDecimal(rdr["total_cost"]),
                                items_raw        = rdr["items_raw"]?.ToString() ?? "—",
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetServedByTable error: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Moves a set of orders (by their IDs) to a new status in one statement.
        /// </summary>
        public Models.Message UpdateMultipleOrderStatus(List<int> orderIds, string newStatus)
        {
            var msg = new Models.Message();
            if (orderIds == null || orderIds.Count == 0)
            {
                msg.message = "Error: No order IDs provided.";
                return msg;
            }
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    var paramNames = orderIds.Select((_, i) => $"@id{i}").ToArray();
                    string sql = $"UPDATE `Order` SET status = @s, updated_at = NOW() WHERE id IN ({string.Join(",", paramNames)})";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@s", newStatus);
                        for (int i = 0; i < orderIds.Count; i++)
                            cmd.Parameters.AddWithValue(paramNames[i], orderIds[i]);
                        int rows = cmd.ExecuteNonQuery();
                        if (rows > 0)
                            SyncTableAvailabilityForOrders(conn, orderIds, newStatus);
                        msg.message = rows > 0 ? "Success" : "Error: No orders were updated.";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("UpdateMultipleOrderStatus error: " + ex.Message);
            }
            return msg;
        }

        /// <summary>
        /// Moves an order to a new status. Returns "Success" or an error string.
        /// </summary>
        public Models.Message UpdateOrderStatus(int orderId, string newStatus)
        {
            var msg = new Models.Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(
                        "UPDATE `Order` SET status = @s, updated_at = NOW() WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@s",   newStatus);
                        cmd.Parameters.AddWithValue("@id",  orderId);
                        int rows = cmd.ExecuteNonQuery();
                        if (rows > 0)
                            SyncTableAvailabilityForOrders(conn, new List<int> { orderId }, newStatus);
                        msg.message = rows > 0 ? "Success" : "Error: Order not found";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("UpdateOrderStatus error: " + ex.Message);
            }
            return msg;
        }

        /// <summary>
        /// All dining tables (<c>table_lists</c>) with QR availability flags — cashier dashboard.
        /// </summary>
        public List<dynamic> GetTableAvailabilityListForCashier()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureUnifiedTableLists(conn);
                    EnsureTableListsHasAvailability(conn);
                    const string tblSql =
                        @"SELECT id AS table_list_id,
                                 COALESCE(CAST(table_number AS CHAR), TRIM(table_name)) AS table_name,
                                 COALESCE(is_available, 1) AS is_available
                          FROM table_lists
                          WHERE table_number IS NOT NULL
                          ORDER BY table_number ASC";
                    using (var cmd = new MySqlCommand(tblSql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                table_list_id = Convert.ToInt32(rdr["table_list_id"]),
                                table_name = rdr["table_name"]?.ToString() ?? "?",
                                is_available = rdr["is_available"] == DBNull.Value ? 1 : Convert.ToInt32(rdr["is_available"])
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTableAvailabilityListForCashier error: " + ex.Message);
            }

            return list;
        }

        public Message SetTableAvailabilityForCashier(int tableListId, bool available)
        {
            var msg = new Message();
            if (tableListId <= 0)
            {
                msg.message = "Error: Invalid table.";
                return msg;
            }

            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureTableListsHasAvailability(conn);
                    using (var cmd = new MySqlCommand(
                               "UPDATE table_lists SET is_available = @av WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@av", available ? 1 : 0);
                        cmd.Parameters.AddWithValue("@id", tableListId);
                        int n = cmd.ExecuteNonQuery();
                        msg.message = n > 0 ? "Success" : "Error: Table not found.";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("SetTableAvailabilityForCashier error: " + ex.Message);
            }

            return msg;
        }

        /// <summary>Whether QR menu ordering is allowed for this registered table number.</summary>
        public bool IsTableQrAvailable(int tableNumber)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureTableListsHasAvailability(conn);
                    int tid = ResolveTableListIdForOrder(tableNumber, conn);
                    if (tid <= 0) return false;
                    using (var cmd = new MySqlCommand(
                               "SELECT COALESCE(is_available, 1) FROM table_lists WHERE id = @id LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", tid);
                        var o = cmd.ExecuteScalar();
                        return o != null && Convert.ToInt32(o) != 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("IsTableQrAvailable error: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Staff Self-Service

        private static bool _resignApprovalMigrated = false;
        private static readonly object _resignApprovalLock = new();

        private void EnsureResignApprovalColumn(MySqlConnection conn)
        {
            if (_resignApprovalMigrated) return;
            lock (_resignApprovalLock)
            {
                if (_resignApprovalMigrated) return;
                try
                {
                    using var check = new MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.columns " +
                        "WHERE table_schema = DATABASE() AND table_name = 'resigns' AND column_name = 'approval_status'", conn);
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    {
                        using var alter = new MySqlCommand(
                            "ALTER TABLE resigns ADD COLUMN approval_status VARCHAR(20) NOT NULL DEFAULT 'Pending'", conn);
                        alter.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EnsureResignApprovalColumn error: " + ex.Message);
                }
                _resignApprovalMigrated = true;
            }
        }

        public Models.Staff? GetMyStatus(int staffId)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    const string sql = @"
                        SELECT r.id, r.registration_name AS name, r.email, r.phone_no, r.address, r.photo,
                               ro.role_name,
                               CASE WHEN rs.registration_id IS NULL THEN 'In Service' ELSE 'Resigned' END AS status
                        FROM registrations r
                        JOIN roles ro ON ro.id = r.role_id
                        LEFT JOIN resigns rs ON rs.registration_id = r.id
                            AND COALESCE(rs.approval_status, 'Pending') != 'Rejected'
                        WHERE r.id = @id
                        LIMIT 1";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", staffId);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                return new Models.Staff
                                {
                                    id        = Convert.ToInt32(rdr["id"]),
                                    name      = rdr["name"]?.ToString() ?? "",
                                    email     = rdr["email"]?.ToString() ?? "",
                                    phone_no  = rdr["phone_no"]?.ToString() ?? "",
                                    address   = rdr["address"]?.ToString() ?? "",
                                    photo     = rdr["photo"]?.ToString() ?? "",
                                    role_name = rdr["role_name"]?.ToString() ?? "",
                                    status    = rdr["status"]?.ToString() ?? "",
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetMyStatus error: " + ex.Message);
            }
            return null;
        }

        public bool HasResigned(int staffId)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    using (var cmd = new MySqlCommand(
                        "SELECT COUNT(*) FROM resigns WHERE registration_id = @id AND COALESCE(approval_status, 'Pending') != 'Rejected'",
                        conn))
                    {
                        cmd.Parameters.AddWithValue("@id", staffId);
                        var count = Convert.ToInt32(cmd.ExecuteScalar());
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("HasResigned error: " + ex.Message);
            }
            return false;
        }

        public Models.Message SubmitResign(int staffId, string reason)
        {
            var msg = new Models.Message();
            try
            {
                if (HasResigned(staffId))
                {
                    msg.message = "Error: You have already submitted a resignation.";
                    return msg;
                }

                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    using (var cmd = new MySqlCommand(
                        "INSERT INTO resigns (registration_id, reason, resign_at, approval_status) VALUES (@id, @reason, NOW(), 'Pending')", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", staffId);
                        cmd.Parameters.AddWithValue("@reason", reason ?? "");
                        cmd.ExecuteNonQuery();
                        msg.message = "Success";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("SubmitResign error: " + ex.Message);
            }
            return msg;
        }

        public List<dynamic> GetResignApprovals()
        {
            var list = new List<dynamic>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    const string sql = @"
                        SELECT rs.id AS resign_id, rs.registration_id, rs.reason, rs.resign_at AS resign,
                               rs.approval_status, r.registration_name AS name, ro.role_name
                        FROM resigns rs
                        JOIN registrations r ON r.id = rs.registration_id
                        LEFT JOIN roles ro ON ro.id = r.role_id
                        ORDER BY rs.resign_at DESC, rs.id DESC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new
                            {
                                resign_id = rdr["resign_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["resign_id"]),
                                registration_id = rdr["registration_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["registration_id"]),
                                name = rdr["name"]?.ToString() ?? "",
                                role_name = rdr["role_name"]?.ToString() ?? "",
                                reason = rdr["reason"]?.ToString() ?? "",
                                resign = rdr["resign"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["resign"]),
                                approval_status = rdr["approval_status"]?.ToString() ?? "Pending"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetResignApprovals error: " + ex.Message);
            }
            return list;
        }

        public Models.Message SetResignApproval(int resignId, string decision)
        {
            var msg = new Models.Message();
            try
            {
                if (resignId <= 0)
                {
                    msg.message = "Error: Invalid resign id";
                    return msg;
                }

                var normalized = (decision ?? "").Trim().ToLowerInvariant();
                if (normalized != "approve" && normalized != "reject")
                {
                    msg.message = "Error: Invalid decision";
                    return msg;
                }

                var newStatus = normalized == "approve" ? "Approved" : "Rejected";

                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    using (var cmd = new MySqlCommand(
                        "UPDATE resigns SET approval_status = @status WHERE id = @resignId AND LOWER(COALESCE(approval_status, 'Pending')) = 'pending'", conn))
                    {
                        cmd.Parameters.AddWithValue("@status", newStatus);
                        cmd.Parameters.AddWithValue("@resignId", resignId);
                        var rows = cmd.ExecuteNonQuery();
                        msg.message = rows > 0 ? "Success" : "Error: Request already processed or not found";
                    }
                }
            }
            catch (Exception ex)
            {
                msg.message = "Error: " + ex.Message;
                Console.WriteLine("SetResignApproval error: " + ex.Message);
            }
            return msg;
        }

        #endregion

        #region Auth

        /// <summary>
        /// True when email/password are correct but login must be refused (pending or approved resignation).
        /// </summary>
        public bool IsLoginBlockedByResignation(string email, string password)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    const string sql = @"
                        SELECT COUNT(*) FROM registrations r
                        WHERE r.email = @email AND r.password_hash = @password
                          AND EXISTS (
                              SELECT 1 FROM resigns x
                              WHERE x.registration_id = r.id
                                AND COALESCE(x.approval_status, 'Pending') != 'Rejected'
                          )
                        LIMIT 1";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@password", password);
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("IsLoginBlockedByResignation error: " + ex.Message);
            }

            return false;
        }

        public Models.Staff? LoginStaff(string email, string password)
        {
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    EnsureResignApprovalColumn(conn);
                    const string sql = @"
                        SELECT r.id, r.registration_name AS name, r.email, r.photo,
                               ro.id AS role_id, ro.role_name
                        FROM registrations r
                        JOIN roles ro ON ro.id = r.role_id
                        WHERE r.email = @email AND r.password_hash = @password
                          AND NOT EXISTS (
                              SELECT 1 FROM resigns x
                              WHERE x.registration_id = r.id
                                AND COALESCE(x.approval_status, 'Pending') != 'Rejected'
                          )
                        LIMIT 1";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@password", password);

                        using (var rst = cmd.ExecuteReader())
                        {
                            if (rst.Read())
                            {
                                return new Models.Staff
                                {
                                    id        = rst["id"] != DBNull.Value ? Convert.ToInt32(rst["id"]) : 0,
                                    name      = rst["name"]?.ToString() ?? "",
                                    email     = rst["email"]?.ToString() ?? "",
                                    photo     = rst["photo"]?.ToString() ?? "",
                                    role_id   = rst["role_id"] != DBNull.Value ? Convert.ToInt32(rst["role_id"]) : 0,
                                    role_name = rst["role_name"]?.ToString() ?? "",
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("LoginStaff error: " + ex.Message);
            }

            return null;
        }

        #endregion
    }
}