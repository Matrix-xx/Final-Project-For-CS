using FoodOutlet.AppCode;
using Microsoft.AspNetCore.Mvc;
using MySqlX.XDevAPI.Common;


namespace FoodOutlet.Controllers
{
    public class EntryController : Controller
    {
        private readonly Staff _staff;

        public EntryController(Staff staff)
        {
            _staff = staff;
        }

        #region view

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

        public IActionResult StaffResignRecords()
        {
            return View();
        }

        public IActionResult Category()
        {
            return View();
        }

        public IActionResult Recipe(int? id)
        {
            if (id.HasValue)
            {
                // Load recipe by ID for editing
                var recipes = _staff.GetAllRecipes();
                var recipe = recipes.FirstOrDefault(r => r.id == id.Value);
                ViewData["Title"] = recipe?.name ?? "Recipe";
                return View(recipe);
            }
            ViewData["Title"] = "Recipe";
            return View();
        }

        #endregion

        #region Staff

        [HttpPost("api/set_staff")]
        public Models.Message SetStaff([FromBody] Models.Staff staff)
        {
            // if id present update, otherwise insert
            if (staff.id > 0)
                return _staff.UpdateStaff(staff);
            else
                return _staff.SetStaff(staff);
        }

        [HttpPost("api/delete_staff")]
        public Models.Message DeleteStaff([FromBody] dynamic payload)
        {
            int id = payload.id;
            return _staff.DeleteStaff(id);
        }

        #endregion

        #region Debug

        // temporary debugging helper: returns column names in registrations table
        [HttpGet("api/registration_columns")]
        public Dictionary<string, dynamic> GetRegistrationColumns()
        {
            var cols = _staff.GetRegistrationColumns();
            return new Dictionary<string, dynamic> { { "columns", cols } };
        }

        #endregion

        #region Role

        [HttpGet("api/get_all_roles")]
        public Dictionary<string, dynamic> GetAllRoles()
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();

            result.Add("roles", _staff.GetAllRoles());

            return result;
        }

        [HttpGet("api/get_all_staffs")]
        public Dictionary<string, dynamic> GetAllStaffs()
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();

            result.Add("staff", _staff.GetAllStaffs());

            return result;
        }

        [HttpGet("api/get_all_categories")]
        public Dictionary<string, dynamic> GetAllCategories()
        {
            return new Dictionary<string, dynamic> { { "categories", _staff.GetAllCategories() } };
        }

        [HttpPost("api/set_category")]
        public Models.Message SetCategory([FromBody] Models.Category cat)
        {
            return _staff.SetCategory(cat);
        }

        [HttpPost("api/delete_category")]
        public Models.Message DeleteCategory([FromBody] dynamic payload)
        {
            int id = payload.id;
            return _staff.DeleteCategory(id);
        }

        [HttpGet("api/get_all_recipes")]
        public Dictionary<string, dynamic> GetAllRecipes()
        {
            return new Dictionary<string, dynamic> { { "recipes", _staff.GetAllRecipes() } };
        }

        [HttpPost("api/set_recipe")]
        public Models.Message SetRecipe([FromBody] Models.Recipe r)
        {
            return _staff.SetRecipe(r);
        }

        [HttpPost("api/delete_recipe")]
        public Models.Message DeleteRecipe([FromBody] dynamic payload)
        {
            int id = payload.id;
            return _staff.DeleteRecipe(id);
        }

        // debugging helper for resign table: name, columns, count, raw rows
        [HttpGet("api/resign_debug")]
        public Dictionary<string, dynamic> ResignDebug()
        {
            return new Dictionary<string, dynamic>
            {
                { "table", _staff.GetResignTableName() },
                { "columns", _staff.GetResignColumns() },
                { "count", _staff.GetResignCount() },
                { "rows", _staff.GetRawResigns() }
            };
        }

        // an alias endpoint matching the new service method name
        [HttpGet("api/get_resigned_staff")]
        public Dictionary<string, dynamic> GetResignedStaff()
        {
            return new Dictionary<string, dynamic> { { "records", _staff.GetResignedStaffs() } };
        }

        [HttpPost("api/set_role")]
        public Models.Message SetRole([FromBody] Models.Role role)
        {
            return _staff.SetRole(role);
        }

        [HttpPost("api/delete_role")]
        public Models.Message DeleteRole([FromBody] dynamic payload)
        {
            int id = payload.id;
            return _staff.DeleteRole(id);
        }

        #endregion

        // recipe debug endpoint
        [HttpGet("api/recipe_debug")]
        public Dictionary<string, dynamic> RecipeDebug()
        {
            return new Dictionary<string, dynamic>
            {
                { "table", _staff.GetRecipeTableName() },
                { "columns", _staff.GetRecipeColumns() },
                { "count", _staff.GetRecipeCount() }
            };
        }

    }
}
