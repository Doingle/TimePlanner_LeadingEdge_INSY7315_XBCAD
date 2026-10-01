using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class CategoriesController : ApiControllerBase
    {
        private readonly ICategoryRepository _categories;

        public CategoriesController(ICategoryRepository categories) => _categories = categories;

        //-----------------------------
        //the whole activity tree as a flat list, clients rebuild the tree from ParentId
        [HttpGet]
        public async Task<ActionResult<List<CategoryDto>>> GetAll() =>
            (await _categories.GetAllAsync())
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto(c.CategoryId, c.Name, c.ParentCategoryId, c.Colour, c.SortOrder, c.IsBillable))
                .ToList();
    }
}
//------------------------------EOF-----------------------------\\
