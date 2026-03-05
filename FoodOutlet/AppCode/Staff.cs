using MySql.Data.MySqlClient;
using FoodOutlet.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Net;
using System.Linq.Expressions;

namespace FoodOutlet.AppCode
{                       
    public class Staff
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public Staff(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

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
                                role.role_name = rst["role_name"].ToString();

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

        public Message SetStaff(Models.Staff staf)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    if (staf.id > 0)
                    {
                        using (MySqlCommand cmd = new MySqlCommand("UPDATE registrations SET name=@name,email=@email,birth_of_date=@birth_of_date,password_hash=@password_hash,phone_no=@phone_no,address=@address,role_id=@role_id WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", staf.id);
                            cmd.Parameters.AddWithValue("@name", staf.name);
                            cmd.Parameters.AddWithValue("@email", staf.email);
                            cmd.Parameters.AddWithValue("@birth_of_date", staf.birth_of_date);
                            cmd.Parameters.AddWithValue("@password_hash", staf.password);
                            cmd.Parameters.AddWithValue("@phone_no", staf.phone_no);
                            cmd.Parameters.AddWithValue("@address", staf.address);
                            cmd.Parameters.AddWithValue("@role_id", staf.role_id);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                    else
                    {
                        using (MySqlCommand cmd = new MySqlCommand("INSERT INTO registrations (name,email,birth_of_date,password_hash,phone_no,address,role_id) VALUES (@name,@email,@birth_of_date,@password_hash,@phone_no,@address,@role_id)", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", staf.name);
                            cmd.Parameters.AddWithValue("@email", staf.email);
                            cmd.Parameters.AddWithValue("@birth_of_date", staf.birth_of_date);
                            cmd.Parameters.AddWithValue("@password_hash", staf.password);
                            cmd.Parameters.AddWithValue("@phone_no", staf.phone_no);
                            cmd.Parameters.AddWithValue("@address", staf.address);
                            cmd.Parameters.AddWithValue("@role_id", staf.role_id);
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

        // helper to check whether reader contains a column
        private bool HasColumn(MySqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public List<Models.Staff> GetAllStaffs()
        {
            List<Models.Staff> staffList = new List<Models.Staff>();

            using (MySqlConnection conn = _connectionFactory.CreateConnection())
            {
                conn.Open();

                // determine which columns exist in registrations table so we only query them
                var cols = GetRegistrationColumns();
                bool hasEmail = cols.Contains("email", StringComparer.OrdinalIgnoreCase);
                bool hasPhone = cols.Contains("phone_no", StringComparer.OrdinalIgnoreCase);
                bool hasAddress = cols.Contains("address", StringComparer.OrdinalIgnoreCase);

                // figure out how resigns table refers to registration key
                var rcols = GetResignColumns();
                string keyCol;
                if (rcols.Contains("registration_id", StringComparer.OrdinalIgnoreCase))
                    keyCol = "registration_id";
                else if (rcols.Contains("resigned", StringComparer.OrdinalIgnoreCase))
                    keyCol = "resigned"; // fallback if only this name exists
                else
                    keyCol = "registration_id"; // default hope for the best

                var selectCols = new List<string> { "r.id", "r.registration_name", "r.birth_of_date", "r.role_id" };
                if (hasEmail) selectCols.Add("r.email");
                if (hasPhone) selectCols.Add("r.phone_no");
                if (hasAddress) selectCols.Add("r.address");
                selectCols.Add($"CASE WHEN rs.{keyCol} IS NULL THEN 'In Service' ELSE 'Resign' END AS status");

                string table = GetResignTableName();
                string query = $"SELECT {string.Join(",", selectCols)} FROM registrations r LEFT JOIN {table} rs ON r.id = rs.{keyCol} ORDER BY r.id";

                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                using (MySqlDataReader rst = cmd.ExecuteReader())
                {
                    while (rst.Read())
                    {
                        Models.Staff staff = new Models.Staff
                        {
                            id = rst["id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["id"]),
                            name = rst["registration_name"]?.ToString() ?? "",
                            email = hasEmail && HasColumn(rst, "email") ? rst["email"]?.ToString() ?? "" : "",
                            birth_of_date = rst["birth_of_date"] == DBNull.Value ? null : Convert.ToDateTime(rst["birth_of_date"]),
                            phone_no = hasPhone && HasColumn(rst, "phone_no") ? rst["phone_no"]?.ToString() ?? "" : "",
                            address = hasAddress && HasColumn(rst, "address") ? rst["address"]?.ToString() ?? "" : "",
                            role_id = rst["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["role_id"]),
                            status = rst["status"]?.ToString() ?? ""
                        };

                        staffList.Add(staff);
                    }
                }
            }

            return staffList;
        }

        // lookup single staff
        public Models.Staff GetStaffById(int id)
        {
            using (MySqlConnection conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT id,registration_name AS name,email,password_hash,birth_of_date,phone_no,address,role_id FROM registrations WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader rst = cmd.ExecuteReader())
                    {
                        if (rst.Read())
                        {
                            return new Models.Staff
                            {
                                id = rst["id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["id"]),
                                name = rst["name"]?.ToString() ?? "",
                                email = HasColumn(rst, "email") ? rst["email"]?.ToString() ?? "" : "",
                                password = HasColumn(rst, "password_hash") ? rst["password_hash"]?.ToString() ?? "" : "",
                                birth_of_date = rst["birth_of_date"] == DBNull.Value ? null : Convert.ToDateTime(rst["birth_of_date"]),
                                phone_no = rst["phone_no"] == DBNull.Value ? "" : rst["phone_no"].ToString(),
                                address = rst["address"]?.ToString() ?? "",
                                role_id = rst["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rst["role_id"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        // update existing staff
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

        // delete staff record
        public Message DeleteStaff(int id)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("DELETE FROM registrations WHERE id=@id", conn))
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

        // debugging helpers
        public List<string> GetRegistrationColumns()
        {
            List<string> cols = new List<string>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='registrations'", conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        cols.Add(rdr.GetString(0));
                    }
                }
            }
            return cols;
        }

        // debugging helper to dump raw resigns rows
        public List<Dictionary<string, object>> GetRawResigns()
        {
            var result = new List<Dictionary<string, object>>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT * FROM resigns", conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < rdr.FieldCount; i++)
                        {
                            row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
                        }
                        result.Add(row);
                    }
                }
            }
            return result;
        }

        // convenience to surface any counts or elements quickly
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

        // return the name of the table that actually exists in the database (resigns vs resign)
        public string GetResignTableName()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('resigns','resign') LIMIT 1", conn))
                {
                    var t = cmd.ExecuteScalar();
                    if (t != null)
                        return t.ToString();
                }
            }
            // fallback to plural, which has been used historically
            return "resigns";
        }

        public List<string> GetResignColumns()
        {
            var cols = new List<string>();
            string table = GetResignTableName();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}'", conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read()) cols.Add(rdr.GetString(0));
                }
            }
            return cols;
        }

        public List<dynamic> GetResignRecords()
        {
            var list = new List<dynamic>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                var rcols = GetResignColumns();
                string keyCol;
                if (rcols.Contains("registration_id", StringComparer.OrdinalIgnoreCase))
                    keyCol = "registration_id";
                else if (rcols.Contains("resigned", StringComparer.OrdinalIgnoreCase))
                    keyCol = "resigned";
                else
                    keyCol = "registration_id";
                string sql = $@"SELECT r.id,r.name,r.email,r.phone_no,r.address,r.role_id,rs.reason,rs.resign
                               FROM registrations r
                               INNER JOIN resigns rs ON r.id = rs.{keyCol}
                               ORDER BY rs.resign DESC";
                try
                {
                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                name = rdr["name"]?.ToString(),
                                email = HasColumn(rdr, "email") ? rdr["email"]?.ToString() : "",
                                phone_no = HasColumn(rdr, "phone_no") ? rdr["phone_no"]?.ToString() : "",
                                address = HasColumn(rdr, "address") ? rdr["address"]?.ToString() : "",
                                role_id = rdr["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["role_id"]),
                                reason = rdr["reason"]?.ToString(),
                                resign = rdr["resign"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["resign"])
                            });
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    Console.WriteLine($"SQL Error in GetResignRecords: {ex.Message}");
                }
            }
            return list;
        }                                                   

        // return resigned staff details by joining on registration_id
        public List<dynamic> GetResignedStaffs()
        {
            var list = new List<dynamic>();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                var rcols = GetResignColumns();
                // decide foreign key column between registrations and resigns
                string keyCol;
                if (rcols.Contains("registration_id", StringComparer.OrdinalIgnoreCase))
                    keyCol = "registration_id";
                else if (rcols.Contains("resigned", StringComparer.OrdinalIgnoreCase))
                    keyCol = "resigned";
                else
                    keyCol = "registration_id";
                // pick correct date column
                string dateCol = "resigned";
                if (rcols.Contains("resign_date", StringComparer.OrdinalIgnoreCase)) dateCol = "resign_date";
                else if (rcols.Contains("resigned_date", StringComparer.OrdinalIgnoreCase)) dateCol = "resigned_date";
                else if (rcols.Contains("resign_at", StringComparer.OrdinalIgnoreCase)) dateCol = "resign_at";

                string table = GetResignTableName();
                string sql = $@"SELECT r.id,r.registration_name,r.email,r.phone_no,r.address,r.role_id,rs.reason,rs.{dateCol} AS resign
                               FROM registrations r
                               INNER JOIN {table} rs ON r.id = rs.{keyCol} ORDER BY rs.{dateCol} DESC";
                using (var cmd = new MySqlCommand(sql, conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new {
                            id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                            name = rdr["registration_name"]?.ToString(),
                            email = HasColumn(rdr, "email") ? rdr["email"]?.ToString() : "",
                            phone_no = HasColumn(rdr, "phone_no") ? rdr["phone_no"]?.ToString() : "",
                            address = HasColumn(rdr, "address") ? rdr["address"]?.ToString() : "",
                            role_id = rdr["role_id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["role_id"]),
                            reason = rdr["reason"]?.ToString(),
                            resign = rdr["resign"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["resign"])
                        });
                    }
                }
            }
            return list;
        }

        // role methods
        public Message SetRole(Models.Role role)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("INSERT INTO roles (role_name) VALUES (@role_name)", conn))
                    {
                        cmd.Parameters.AddWithValue("@role_name", role.role_name);
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

        public Message DeleteRole(int id)
        {
            Message msg = new Message();
            try
            {
                using (MySqlConnection conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("DELETE FROM roles WHERE id = @id", conn))
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

        // category methods
        public List<Models.Category> GetAllCategories()
        {
            var list = new List<Models.Category>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("SELECT id, category_name FROM categories ORDER BY id", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new Models.Category {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                category_name = rdr["category_name"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
            {
                Console.WriteLine($"Error loading categories: {ex.Message}");
                // return empty list if database not available
            }
            return list;
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

        // recipe methods (simplified - only name and price)
        public List<Models.Recipe> GetAllRecipes()
        {
            var list = new List<Models.Recipe>();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    string table = GetRecipeTableName();

                    string sql = $@"
                        SELECT r.id,
                               r.recipe_name AS name,
                               r.price
                        FROM {table} r
                        ORDER BY r.id";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new Models.Recipe
                            {
                                id = rdr["id"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["id"]),
                                name = rdr["name"]?.ToString() ?? "",
                                price = rdr["price"] == DBNull.Value ? 0 : Convert.ToDecimal(rdr["price"]),
                                qty = 0,
                                qty_status = ""
                            });
                        }
                    }
                }
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
            {
                Console.WriteLine($"Error loading recipes: {ex.Message}");
            }
            return list;
        }

        public Message SetRecipe(Models.Recipe r)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    string table = GetRecipeTableName();

                    if (r.id > 0)
                    {
                        using (var cmd = new MySqlCommand($"UPDATE {table} SET recipe_name=@name, price=@price WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", r.id);
                            cmd.Parameters.AddWithValue("@name", r.name ?? "");
                            cmd.Parameters.AddWithValue("@price", r.price);
                            cmd.ExecuteNonQuery();
                            msg.message = "Success";
                        }
                    }
                    else
                    {
                        using (var cmd = new MySqlCommand($"INSERT INTO {table} (recipe_name, price, created_at) VALUES (@name, @price, NOW())", conn))
                        {
                            cmd.Parameters.AddWithValue("@name", r.name ?? "");
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

        public Message DeleteRecipe(int id)
        {
            Message msg = new Message();
            try
            {
                using (var conn = _connectionFactory.CreateConnection())
                {
                    conn.Open();
                    string table = GetRecipeTableName();

                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmdInv = new MySqlCommand("DELETE FROM inventories WHERE recipe_id = @rid", conn, tx))
                        {
                            cmdInv.Parameters.AddWithValue("@rid", id);
                            cmdInv.ExecuteNonQuery();
                        }

                        using (var cmd = new MySqlCommand($"DELETE FROM {table} WHERE id=@id", conn, tx))
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

        // helpers for recipe diagnostics
        public string GetRecipeTableName()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('recipes','recipe') LIMIT 1", conn))
                {
                    var t = cmd.ExecuteScalar();
                    if (t != null)
                        return t.ToString();
                }
            }
            return "recipes";
        }

        public List<string> GetRecipeColumns()
        {
            var cols = new List<string>();
            string table = GetRecipeTableName();
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}'", conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read()) cols.Add(rdr.GetString(0));
                }
            }
            return cols;
        }

        public int GetRecipeCount()
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                string table = GetRecipeTableName();
                using (var cmd = new MySqlCommand($"SELECT COUNT(*) FROM {table}", conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

    }
}