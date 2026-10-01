using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class ProjectsController : ApiControllerBase
    {
        private readonly IProjectRepository _projects;

        public ProjectsController(IProjectRepository projects) => _projects = projects;

        //-----------------------------
        //projects with their company, optionally only one company and/or only projects still accepting time
        [HttpGet]
        public async Task<ActionResult<List<ProjectDto>>> GetAll([FromQuery] int? companyId, [FromQuery] bool activeOnly = false)
        {
            var projects = activeOnly ? await _projects.GetActiveAsync() : await _projects.GetAllAsync();
            return projects
                .Where(p => companyId == null || p.CompanyId == companyId)
                .Select(ToDto)
                .ToList();
        }

        //-----------------------------
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProjectDto>> Get(int id)
        {
            var project = await _projects.GetByIdAsync(id);
            return project == null ? NotFound() : ToDto(project);
        }

        private static ProjectDto ToDto(Project p) =>
            new(p.ProjectID, p.Name, p.CompanyId, p.Company?.Name ?? string.Empty, p.Status.ToString(), p.Colour);
    }
}
//------------------------------EOF-----------------------------\\
