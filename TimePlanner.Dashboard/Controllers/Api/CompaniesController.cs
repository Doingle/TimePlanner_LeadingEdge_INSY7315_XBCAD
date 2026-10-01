using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class CompaniesController : ApiControllerBase
    {
        private readonly ICompanyRepository _companies;

        public CompaniesController(ICompanyRepository companies) => _companies = companies;

        //-----------------------------
        //every company, ordered by name
        [HttpGet]
        public async Task<ActionResult<List<CompanyDto>>> GetAll() =>
            (await _companies.GetAllAsync()).Select(c => new CompanyDto(c.CompanyId, c.Name)).ToList();
    }
}
//------------------------------EOF-----------------------------\\
