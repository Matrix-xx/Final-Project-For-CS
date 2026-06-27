using FoodOutlet.AppCode;
using FoodOutlet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using QRCoder;
using System.Text.Json;

namespace FoodOutlet.Controllers
{
    [Authorize]
    public class EntryController : Controller
    {
        private readonly AppCode.Staff _staff;
        private readonly IWebHostEnvironment _env;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ImageProcessingService _imageService; // â† ADD THIS

        public EntryController(AppCode.Staff staff, IWebHostEnvironment env, IDbConnectionFactory connectionFactory, ImageProcessingService imageService)
        {
            _staff = staff;
            _env = env;
            _connectionFactory = connectionFactory;
            _imageService = imageService; // â† ADD THIS
        }

        private void DebugLog(string runId, string hypothesisId, string location, string message, object data)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    sessionId = "3b1609",
                    runId,
                    hypothesisId,
                    location,
                    message,
                    data,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                System.IO.File.AppendAllText("debug-3b1609.log", payload + Environment.NewLine);
            }
            catch
            {
            }
        }

        #region Existing Views
        [Authorize(Policy = "AdminOrChef")]
        public IActionResult Inventory()
        {
            // #region agent log
            DebugLog("post-fix", "H5", "Controllers/EntryController.cs:Inventory", "inventory page opened", new { role = User.FindFirst("RoleName")?.Value ?? "" });
            // #endregion
            return View();
        }
        public IActionResult Role()
        {
            return View();
        }

        public IActionResult Registration(int? id)
        {
            if (id.HasValue)
            {
                var staff = _staff.GetStaffById(id.Value);
                ViewData["Title"] = staff?.name ?? "Registration";
                return View(staff);
            }
            ViewData["Title"] = "Registration";
            return View();
        }

        public IActionResult StaffList()
        {
            return View();
        }

        public IActionResult ResignApproval()
        {
            return View();
        }

        public IActionResult OrderHistory()
        {
            return View();
        }

        /// <summary>
        /// Table order history — paid/completed (<c>Done</c>) orders only.
        /// </summary>
        public IActionResult TableOrderHistory()
        {
            ViewData["Title"] = "Table Order History";
            return View();
        }

        public IActionResult Category()
        {
            return View();
        }

        public IActionResult Recipe(int? id)
        {
            ViewData["Categories"] = _staff.GetAllCategories();
            if (id.HasValue)
            {
                var recipe = _staff.GetRecipeById(id.Value);
                ViewData["Title"] = recipe?.recipe_name ?? "Recipe";
                return View(recipe);
            }
            ViewData["Title"] = "Recipe";
            return View();
        }

        public IActionResult RecipeList()
        {
            return View();
        }
        #endregion

        #region Table Registration (Admin) and Customer Menu

        // GET: /Entry/TableRegistration - Display table list and form
        public IActionResult TableRegistration()
        {
            return View(_staff.GetAllRegisteredTables());
        }

        // POST: /Entry/CreateTable - Generate QR and save table
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateTable(int table_number)
        {
            // Backend validation - check if number is valid
            if (!ModelState.IsValid)
            {
                return View("TableRegistration", _staff.GetAllRegisteredTables());
            }

            // Additional validation - table number must be positive
            if (table_number <= 0)
            {
                ModelState.AddModelError("table_number", "Table number must be greater than 0");
                return View("TableRegistration", _staff.GetAllRegisteredTables());
            }

            if (_staff.RegisteredTableNumberExists(table_number))
            {
                TempData["Error"] = $"Table #{table_number} already exists. QR is stored for this table.";
                return RedirectToAction(nameof(TableRegistration));
            }

            try
            {
                var url = $"{Request.Scheme}://{Request.Host}/table/{table_number}";

                // Create folder if it doesn't exist
                var folder = Path.Combine(_env.WebRootPath ?? "wwwroot", "tableQR");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                // Generate QR code file with format: {guid}_table{number}.jpg
                var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
                var fileName = $"{guid}_table{table_number}.jpg";
                var filePath = Path.Combine(folder, fileName);

                // Generate QR code using QRCoder
                var qrGenerator = new QRCodeGenerator();
                var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                var pngWriter = new PngByteQRCode(qrData);
                var pngBytes = pngWriter.GetGraphic(20);

                // Save QR image to file
                System.IO.File.WriteAllBytes(filePath, pngBytes);

                var relativePath = $"/tableQR/{fileName}";

                var result = _staff.InsertRegisteredTable(table_number, relativePath);
                if (result.message != "Success")
                {
                    TempData["Error"] = result.message;
                    return RedirectToAction(nameof(TableRegistration));
                }

                TempData["Success"] = $"Table #{table_number} created successfully. QR code generated and ready to scan!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
            }

            return RedirectToAction(nameof(TableRegistration));
        }

        // POST: /Entry/DeleteTable - Delete table and QR file
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteTable(int id)
        {
            try
            {
                var qrPath = _staff.GetRegisteredTableQrPath(id) ?? "";
                var result = _staff.DeleteRegisteredTable(id);
                if (result.message != "Success")
                {
                    TempData["Error"] = result.message;
                    return RedirectToAction(nameof(TableRegistration));
                }

                // Delete QR file if it exists
                if (!string.IsNullOrEmpty(qrPath))
                {
                    var physical = Path.Combine(_env.WebRootPath ?? "wwwroot", qrPath.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(physical))
                    {
                        System.IO.File.Delete(physical);
                    }
                }

                TempData["Success"] = "Table and QR deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
            }

            return RedirectToAction(nameof(TableRegistration));
        }

        // GET: /table/{tableNumber} - Customer Menu Page
        [AllowAnonymous]
        [HttpGet("table/{tableNumber}")]
        public IActionResult Table(int tableNumber)
        {
            if (!_staff.RegisteredTableNumberExists(tableNumber))
                return NotFound();

            // Load recipes and stock
            var recipes = _staff.GetAllRecipes();
            var inventories = _staff.GetAllInventories();
            var stockByRecipe = inventories.ToDictionary(
                i => (int)i.recipe_id,
                i => (int)i.stock_qty
            );

            // Convert recipe images to base64 if they are file paths
            var converted = new List<dynamic>();
            foreach (var r in recipes)
            {
                string img = r.recipe_img ?? "";

                if (!string.IsNullOrEmpty(img) && !img.StartsWith("data:"))
                {
                    try
                    {
                        var physical = Path.Combine(_env.WebRootPath ?? "wwwroot", img.TrimStart('/', '\\'));
                        if (System.IO.File.Exists(physical))
                        {
                            var bytes = System.IO.File.ReadAllBytes(physical);
                            string mime = "image/png";
                            var ext = Path.GetExtension(physical).ToLowerInvariant();

                            if (ext == ".jpg" || ext == ".jpeg")
                                mime = "image/jpeg";
                            else if (ext == ".gif")
                                mime = "image/gif";
                            else if (ext == ".webp")
                                mime = "image/webp";

                            var base64 = Convert.ToBase64String(bytes);
                            img = $"data:{mime};base64,{base64}";
                        }
                    }
                    catch
                    {
                        // Keep original path if conversion fails
                    }
                }

                int recipeId = (int)r.id;
                int stock = stockByRecipe.ContainsKey(recipeId) ? stockByRecipe[recipeId] : 0;

                converted.Add(new
                {
                    id = r.id,
                    recipe_name = r.recipe_name,
                    category_id = r.category_id,
                    recipe_img = img,
                    description = r.description,
                    ingredients = r.ingredients,
                    price = r.price,
                    category_name = r.category_name,
                    stock_qty = stock
                });
            }

            var latestItem = converted
                .OrderByDescending(x => (int)x.id)
                .FirstOrDefault();

            ViewData["LatestItemName"] = latestItem == null ? "New Items" : (string)latestItem.recipe_name;
            ViewData["LatestItemImage"] = latestItem == null ? "/img/sticky-rice.jpg" : (string)latestItem.recipe_img;
            ViewData["LatestItemDesc"] = latestItem == null ? "No item available" : (string)latestItem.description;
            ViewData["TableNumber"] = tableNumber;
            ViewData["TableQrAvailable"] = _staff.IsTableQrAvailable(tableNumber);
            return View("~/Views/Entry/Table.cshtml", converted);
        }

        // GET: /table/{tableNumber}/cart - Customer Cart Page
        [AllowAnonymous]
        [HttpGet("table/{tableNumber}/cart")]
        public IActionResult Cart(int tableNumber)
        {
            if (!_staff.RegisteredTableNumberExists(tableNumber))
                return NotFound();

            ViewData["TableNumber"] = tableNumber;
            ViewData["TableQrAvailable"] = _staff.IsTableQrAvailable(tableNumber);
            return View("~/Views/Entry/Cart.cshtml");
        }

        #endregion

        #region Existing API Endpoints (unchanged)

        [HttpGet("api/get_all_roles")]
        public Dictionary<string, dynamic> GetAllRoles()
        {
            var result = new Dictionary<string, dynamic>();
            result.Add("roles", _staff.GetAllRoles());
            return result;
        }

        [HttpGet("api/get_all_staffs")]
        public Dictionary<string, dynamic> GetAllStaffs()
        {
            return new Dictionary<string, dynamic> { { "staff", _staff.GetAllStaffs() } };
        }

        [AllowAnonymous]
        [HttpGet("api/get_all_categories")]
        public Dictionary<string, dynamic> GetAllCategories()
        {
            return new Dictionary<string, dynamic> { { "categories", _staff.GetAllCategories() } };
        }

        [HttpGet("api/get_all_recipes")]
        public Dictionary<string, dynamic> GetAllRecipes()
        {
            return new Dictionary<string, dynamic> { { "recipes", _staff.GetAllRecipes() } };
        }

        [HttpPost("api/set_staff")]
        public Models.Message SetStaff([FromBody] Models.Staff staff)
        {
            return _staff.SetStaff(staff);
        }

        [HttpPost("api/set_category")]
        public Models.Message SetCategory([FromBody] Models.Category cat)
        {
            return _staff.SetCategory(cat);
        }

        [HttpPost("api/upload_recipe_image")]
        public IActionResult UploadRecipeImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded" });

            var uploads = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "recipes");
            if (!Directory.Exists(uploads))
                Directory.CreateDirectory(uploads);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploads, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            var relative = $"/uploads/recipes/{fileName}";
            return Ok(new { imageUrl = relative });
        }

        [HttpGet("api/admin/get_resign_approvals")]
        public Dictionary<string, dynamic> GetResignApprovals()
        {
            // #region agent log
            DebugLog("initial", "H1", "Controllers/EntryController.cs:GetResignApprovals", "endpoint hit", new { route = "/api/admin/get_resign_approvals" });
            // #endregion
            var records = _staff.GetResignApprovals();
            // #region agent log
            DebugLog("initial", "H3", "Controllers/EntryController.cs:GetResignApprovals", "records loaded", new { count = records.Count });
            // #endregion
            return new Dictionary<string, dynamic> { { "records", records } };
        }

        [HttpPost("api/admin/set_resign_approval")]
        public Models.Message SetResignApproval([FromBody] ResignApprovalRequest payload)
        {
            // #region agent log
            DebugLog("initial", "H2", "Controllers/EntryController.cs:SetResignApproval", "request received", new
            {
                resign_id = payload?.resign_id ?? 0,
                decision = payload?.decision ?? ""
            });
            // #endregion
            var result = _staff.SetResignApproval(payload?.resign_id ?? 0, payload?.decision ?? "");
            // #region agent log
            DebugLog("initial", "H2", "Controllers/EntryController.cs:SetResignApproval", "request completed", new { message = result.message });
            // #endregion
            return result;
        }

        [HttpGet("api/get_all_inventories")]
        public Dictionary<string, dynamic> GetAllInventories()
        {
            return new Dictionary<string, dynamic> { { "inventories", _staff.GetAllInventories() } };
        }

        [HttpPost("api/set_recipe")]
        public Models.Message SetRecipe([FromBody] Models.Recipe r)
        {
            return _staff.SetRecipe(r);
        }

        [HttpPost("api/set_role")]
        public Models.Message SetRole([FromBody] Models.Role role)
        {
            return _staff.SetRole(role);
        }

        [HttpPost("api/set_inventory")]
        public Models.Message SetInventory([FromBody] Models.Inventory inv)
        {
            return _staff.SetInventory(inv);
        }

        [HttpPost("api/delete_staff")]
        public Models.Message DeleteStaff([FromBody] DeleteRequest payload)
        {
            int id = payload.id;
            return _staff.DeleteStaff(id);
        }

        [HttpPost("api/delete_category")]
        public Models.Message DeleteCategory([FromBody] DeleteRequest payload)
        {
            int id = payload.id;
            return _staff.DeleteCategory(id);
        }

        [HttpPost("api/delete_recipe")]
        public Models.Message DeleteRecipe([FromBody] DeleteRequest payload)
        {
            int id = payload.id;
            return _staff.DeleteRecipe(id);
        }

        [HttpPost("api/delete_role")]
        public Models.Message DeleteRole([FromBody] DeleteRequest payload)
        {
            int id = payload.id;
            return _staff.DeleteRole(id);
        }

        [Authorize(Policy = "AdminOrChef")]
        [HttpPost("api/delete_inventory")]
        public Models.Message DeleteInventory([FromBody] DeleteRequest payload)
        {
            int id = payload.id;
            return _staff.DeleteInventory(id);
        }

        [AllowAnonymous]
        [HttpPost("api/create_order")]
        public Models.Message CreateOrder([FromBody] Models.CreateOrderRequest request)
        {
            return _staff.CreateOrder(request.table_number, request.items);
        }

        [HttpGet("api/get_counts")]
        public Dictionary<string, dynamic> GetCounts()
        {
            return new Dictionary<string, dynamic>
            {
                { "staff_count", _staff.GetStaffCount() },
                { "category_count", _staff.GetCategoryCount() },
                { "recipe_count", _staff.GetRecipeCount() },
                { "role_count", _staff.GetRoleCount() },
                { "resign_count", _staff.GetResignCount() },
                { "order_history_count", _staff.GetOrderHistoryCount() },
                { "resign_message_count", _staff.GetResignPendingCount() }
            };
        }

        [HttpGet("api/admin/sales_chart")]
        public Dictionary<string, dynamic> GetSalesChart(int days = 7)
        {
            return new Dictionary<string, dynamic> { { "series", _staff.GetSalesChartData(days) } };
        }

        /// <summary>MMK totals per category for completed orders in the sliding window.</summary>
        [HttpGet("api/admin/sales_by_category")]
        public Dictionary<string, dynamic> GetSalesByCategory(int days = 7)
        {
            if (days < 1) days = 7;
            if (days > 366 * 5) days = 366 * 5;
            return new Dictionary<string, dynamic> { { "categories", _staff.GetCategoryIncomeForDashboard(days) } };
        }

        /// <summary>Top-selling items by quantity in the sliding window.</summary>
        [HttpGet("api/admin/top_selling_items")]
        public Dictionary<string, dynamic> GetTopSellingItemsEndpoint(int days = 7, int take = 5)
        {
            if (days < 1) days = 7;
            if (days > 366 * 5) days = 366 * 5;
            if (take < 1) take = 5;
            if (take > 20) take = 20;
            return new Dictionary<string, dynamic> { { "items", _staff.GetTopSellingItems(days, take) } };
        }

        [HttpGet("api/admin/order_history")]
        public Dictionary<string, dynamic> GetAdminOrderHistory()
        {
            return new Dictionary<string, dynamic> { { "orders", _staff.GetOrderHistoryAll() } };
        }

        /// <summary>Rows appear here only after bill is closed (status Done).</summary>
        [HttpGet("api/admin/table_order_history")]
        public Dictionary<string, dynamic> GetAdminTableOrderHistory()
        {
            return new Dictionary<string, dynamic> { { "orders", _staff.GetOrderHistoryPaid() } };
        }

        [HttpGet("api/admin/table_order_detail/{orderId:int}")]
        public IActionResult GetAdminTableOrderDetail(int orderId)
        {
            var detail = _staff.GetTableOrderPaidDetail(orderId);
            if (detail == null) return NotFound(new { message = "Order not found or not paid yet." });
            return Json(detail);
        }

        [HttpGet("api/get_all_tables")]
        public Dictionary<string, dynamic> GetAllTables()
        {
            var tables = _staff.GetAllRegisteredTables()
                .Select(t => (dynamic)new
                {
                    id = t.id,
                    table_number = t.table_number,
                    qr_code = t.qr_code,
                    created_at = t.created_at
                })
                .ToList();

            return new Dictionary<string, dynamic> { { "tables", tables } };
        }

        #endregion

        /// <summary>
        /// New endpoint for uploading staff photos with automatic processing
        /// </summary>
        [HttpPost("api/set_staff_with_photo")]
        public async Task<Models.Message> SetStaffWithPhoto()
        {
            var msg = new Models.Message();
            
            try
            {
                // Read form data
                var id = int.TryParse(Request.Form["id"], out var staffId) ? staffId : 0;
                var name = Request.Form["name"].ToString();
                var email = Request.Form["email"].ToString();
                var phone = Request.Form["phone_no"].ToString();
                var address = Request.Form["address"].ToString();
                var password = Request.Form["password"].ToString();
                var roleId = int.TryParse(Request.Form["role_id"], out var rid) ? rid : 0;
                var birthDate = Request.Form["birth_of_date"].ToString();

                var staff = new Models.Staff
                {
                    id = id,
                    name = name,
                    email = email,
                    phone_no = phone,
                    address = address,
                    password = password,
                    role_id = roleId,
                    birth_of_date = string.IsNullOrEmpty(birthDate) ? null : DateTime.Parse(birthDate)
                };

                // Handle photo upload with processing
                var photoFile = Request.Form.Files["photoFile"];
                if (photoFile != null && photoFile.Length > 0)
                {
                    try
                    {
                        // Process and save image (auto crop to square, resize to 300x300)
                        staff.photo = await _imageService.ProcessAndSaveImageAsync(photoFile, "uploads/staff");
                        
                        Console.WriteLine($"Image processed and saved: {staff.photo}");
                    }
                    catch (Exception ex)
                    {
                        return new Models.Message { message = $"Error: {ex.Message}" };
                    }
                }
                else if (id > 0)
                {
                    // Keep existing photo if not updating
                    var existingStaff = _staff.GetStaffById(id);
                    staff.photo = existingStaff?.photo ?? "";
                }

                // Save to database
                msg = _staff.SetStaff(staff);
            }
            catch (Exception ex)
            {
                msg.message = $"Error: {ex.Message}";
                Console.WriteLine($"SetStaffWithPhoto Exception: {ex}");
            }

            return msg;
        }

        /// <summary>
        /// Returns all active orders for a table (status: Pending/Approved/Ready/Served).
        /// Called by the customer-facing "Order History" panel via AJAX.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("/api/table/order_history")]
        public IActionResult CustomerTableOrderHistory(int tableNumber)
        {
            var history = _staff.GetTableOrderHistory(tableNumber);

            var result = history.Select(o => new
            {
                order_id      = (int)o.order_id,
                status        = (string)o.status,
                created_at    = ((DateTime)o.created_at).ToString("dd MMM HH:mm"),
                order_total   = (decimal)o.order_total,
                items_summary = (string)o.items_summary,
            }).ToList();
            return Json(result);
        }
    }
}
