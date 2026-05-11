using FoodOutlet.AppCode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FoodOutlet.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
        private readonly Staff _staff;

        public OrderController(Staff staff)
        {
            _staff = staff;
        }

        private string Role => User.FindFirst("RoleName")?.Value ?? "";

        private void DebugLog(string runId, string hypothesisId, string location, string message, object data)
        {
            // #region agent log
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
                System.IO.File.AppendAllText(@"D:\MLM\Final\debug-3b1609.log", payload + Environment.NewLine);
            }
            catch
            {
            }
            // #endregion
        }

        // Allowed next-statuses per role + current status
        private static readonly Dictionary<string, Dictionary<string, string[]>> AllowedTransitions =
            new()
            {
                ["Cashier"] = new()
                {
                    ["Pending"]  = new[] { "Approved", "Cancelled" },
                    ["Served"]   = new[] { "Cleaning" },
                },
                ["Chef"] = new()
                {
                    ["Approved"] = new[] { "Ready", "Cancelled" },
                },
                ["Waiter"] = new()
                {
                    ["Ready"]    = new[] { "Served", "Cancelled" },
                },
                ["Cleaner"] = new()
                {
                    ["Cleaning"] = new[] { "Done", "Cancelled" },
                },
            };

        // Statuses each role needs to see
        private static readonly Dictionary<string, string[]> RoleStatuses = new()
        {
            ["Cashier"] = new[] { "Pending", "Served" },
            ["Chef"]    = new[] { "Approved" },
            ["Waiter"]  = new[] { "Ready" },
            ["Cleaner"] = new[] { "Cleaning" },
        };

        public IActionResult Index()
        {
            DebugLog("post-fix", "H1", "Controllers/OrderController.cs:Index", "order index hit", new { role = Role });

            /* Cashier uses dedicated pages (availability / orders / payment). */
            if (Role == "Cashier")
                return RedirectToAction(nameof(CashierOrders));

            if (!RoleStatuses.TryGetValue(Role, out var statuses))
            {
                DebugLog("post-fix", "H1", "Controllers/OrderController.cs:Index", "role not in RoleStatuses", new { role = Role, knownRoles = RoleStatuses.Keys });
                return Forbid();
            }

            DebugLog("post-fix", "H2", "Controllers/OrderController.cs:Index", "role statuses resolved", new { role = Role, statuses });
            var orders = _staff.GetOrdersWithItems(statuses);
            DebugLog("post-fix", "H3", "Controllers/OrderController.cs:Index", "orders loaded for role", new { role = Role, count = orders.Count });
            ViewData["Role"] = Role;

            // Cleaner's Cleaning queue is also grouped by table
            if (Role == "Cleaner")
                ViewData["CleaningByTable"] = _staff.GetCleaningByTable();

            return View(orders);
        }

        /// <summary>Cashier: open/closed per table for QR ordering.</summary>
        public IActionResult CashierAvailability()
        {
            if (Role != "Cashier") return Forbid();
            ViewData["Role"] = Role;
            ViewData["Title"] = "Cashier — Table availability";
            ViewData["CashierTableAvailability"] = _staff.GetTableAvailabilityListForCashier();
            return View();
        }

        /// <summary>Cashier: pending orders (approve/cancel).</summary>
        public IActionResult CashierOrders()
        {
            if (Role != "Cashier") return Forbid();
            ViewData["Role"] = Role;
            ViewData["Title"] = "Cashier — Orders";
            var pending = _staff.GetOrdersWithItems("Pending");
            return View(pending);
        }

        /// <summary>Cashier: served tables awaiting payment then send to clean.</summary>
        public IActionResult CashierPayment()
        {
            if (Role != "Cashier") return Forbid();
            ViewData["Role"] = Role;
            ViewData["Title"] = "Cashier — Payment";
            ViewData["ServedByTable"] = _staff.GetServedByTable();
            return View(new List<dynamic>());
        }

        [HttpPost("api/order/update_status")]
        public IActionResult UpdateStatus([FromBody] UpdateStatusRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            DebugLog("post-fix", "H4", "Controllers/OrderController.cs:UpdateStatus", "update status requested", new { role = Role, order_id = req.order_id, new_status = req.new_status });
            if (!AllowedTransitions.TryGetValue(Role, out var byStatus))
            {
                DebugLog("post-fix", "H4", "Controllers/OrderController.cs:UpdateStatus", "role not in AllowedTransitions", new { role = Role, knownRoles = AllowedTransitions.Keys });
                return StatusCode(403, new { success = false, message = "Not authorised." });
            }

            var orders = _staff.GetOrdersWithItems(byStatus.Keys.ToArray());
            var order  = orders.FirstOrDefault(o => (int)o.order_id == req.order_id);
            if (order == null)
                return NotFound(new { success = false, message = "Order not found or not in your queue." });

            string currentStatus = (string)order.status;
            if (!byStatus.TryGetValue(currentStatus, out var allowed) ||
                !allowed.Contains(req.new_status))
            {
                return BadRequest(new { success = false, message = $"Transition '{currentStatus}' → '{req.new_status}' is not allowed." });
            }

            var result = _staff.UpdateOrderStatus(req.order_id, req.new_status);
            bool ok = result.message == "Success";
            return ok
                ? Ok(new { success = true })
                : StatusCode(500, new { success = false, message = result.message });
        }

        /// <summary>
        /// Bulk-moves all Served orders for a table (identified by their IDs) to the next
        /// status.  Used by the cashier's consolidated table card "Send to Clean" button.
        /// </summary>
        [HttpPost("api/order/update_table_status")]
        public IActionResult UpdateTableStatus([FromBody] UpdateTableStatusRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.order_ids) || string.IsNullOrWhiteSpace(req.new_status))
                return BadRequest(new { success = false, message = "Invalid request." });

            // Validate role is allowed to perform bulk transitions
            var bulkAllowed = new Dictionary<string, string[]>
            {
                ["Cashier"]  = new[] { "Cleaning" },
                ["Cleaner"]  = new[] { "Done", "Cancelled" },
            };
            if (!bulkAllowed.TryGetValue(Role, out var allowed) || !allowed.Contains(req.new_status))
                return StatusCode(403, new { success = false, message = "Not authorised for this transition." });

            // Parse the comma-separated IDs sent from the view
            var ids = req.order_ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out int n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (ids.Count == 0)
                return BadRequest(new { success = false, message = "No valid order IDs." });

            var result = _staff.UpdateMultipleOrderStatus(ids, req.new_status);
            bool ok = result.message == "Success";
            return ok
                ? Ok(new { success = true })
                : StatusCode(500, new { success = false, message = result.message });
        }

        /// <summary>Cashier toggles whether a table accepts new QR orders.</summary>
        [HttpPost("api/order/set_table_availability")]
        public IActionResult SetTableAvailability([FromBody] SetTableAvailabilityRequest? req)
        {
            if (req == null || req.table_list_id <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (Role != "Cashier")
                return StatusCode(403, new { success = false, message = "Only cashiers can change table availability." });

            var result = _staff.SetTableAvailabilityForCashier(req.table_list_id, req.is_available);
            bool ok = result.message == "Success";
            return ok
                ? Ok(new { success = true })
                : StatusCode(500, new { success = false, message = result.message });
        }

        public class UpdateStatusRequest
        {
            public int    order_id   { get; set; }
            public string new_status { get; set; } = "";
        }

        public class UpdateTableStatusRequest
        {
            public string order_ids  { get; set; } = "";
            public string new_status { get; set; } = "";
        }

        public class SetTableAvailabilityRequest
        {
            public int    table_list_id { get; set; }
            public bool   is_available  { get; set; }
        }
    }
}
