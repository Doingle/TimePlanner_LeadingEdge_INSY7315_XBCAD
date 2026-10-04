using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //removes companies projects and activities without losing logged time
    public class CatalogService
    {
        private readonly ICompanyRepository _companies;
        private readonly IProjectRepository _projects;
        private readonly IWorkTaskRepository _tasks;
        private readonly ICategoryRepository _categories;

        public CatalogService(ICompanyRepository companies, IProjectRepository projects, IWorkTaskRepository tasks, ICategoryRepository categories)
        {
            _companies = companies;
            _projects = projects;
            _tasks = tasks;
            _categories = categories;
        }

        //-----------------------------
        //deletes an unused project or closes a used one
        public async Task<RemoveOutcome> RemoveProjectAsync(int projectId)
        {
            var project = await _projects.GetByIdAsync(projectId);

            //a missing project counts as removed
            if (project == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            //internal work always stays
            if (BillingRules.IsInternal(project.Company?.Name))
            {
                return new RemoveOutcome(RemoveResult.NotAllowed, "Internal can't be removed.");
            }

            var outcome = await RemoveProjectCoreAsync(project);
            await DropEmptyCompanyAsync(project.CompanyId);
            return outcome;
        }

        //-----------------------------
        //removes every project then the company if nothing is left
        public async Task<RemoveOutcome> RemoveCompanyAsync(int companyId)
        {
            var company = await _companies.GetByIdAsync(companyId);

            //a missing company counts as removed
            if (company == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            //internal work always stays
            if (BillingRules.IsInternal(company.Name))
            {
                return new RemoveOutcome(RemoveResult.NotAllowed, "Internal can't be removed.");
            }

            var hidden = false;

            //each project is deleted or closed on its own
            foreach (var project in await _projects.GetByCompanyAsync(companyId))
            {
                var outcome = await RemoveProjectCoreAsync(project);

                //any closed project keeps the company
                if (outcome.Result == RemoveResult.Hidden)
                {
                    hidden = true;
                }
            }

            await DropEmptyCompanyAsync(companyId);

            return hidden
                ? new RemoveOutcome(RemoveResult.Hidden, $"{company.Name} has logged time so it was hidden.")
                : new RemoveOutcome(RemoveResult.Deleted, $"{company.Name} was removed.");
        }

        //-----------------------------
        //deletes unused activities and hides used ones under a node
        public async Task<RemoveOutcome> RemoveActivityAsync(int categoryId)
        {
            var all = await _categories.GetAllAsync();
            var target = all.FirstOrDefault(c => c.CategoryId == categoryId);

            //a missing activity counts as removed
            if (target == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            //top level activities are shared and always stay
            if (target.ParentCategoryId == null)
            {
                return new RemoveOutcome(RemoveResult.NotAllowed, "Main activities can't be removed.");
            }

            var kept = await RemoveNodeAsync(target, all);

            return kept
                ? new RemoveOutcome(RemoveResult.Hidden, $"{target.Name} has logged time so it was hidden.")
                : new RemoveOutcome(RemoveResult.Deleted, $"{target.Name} was removed.");
        }

        //-----------------------------
        //closes a used project or deletes an unused one
        private async Task<RemoveOutcome> RemoveProjectCoreAsync(Project project)
        {
            //logged time keeps the project so it is only closed
            if (await _tasks.AnyForProjectAsync(project.ProjectID))
            {
                project.Status = ProjectStatus.Closed;
                await _projects.UpdateAsync(project);
                return new RemoveOutcome(RemoveResult.Hidden, $"{project.Name} has logged time so it was hidden.");
            }

            await _projects.DeleteAsync(project.ProjectID);
            return new RemoveOutcome(RemoveResult.Deleted, $"{project.Name} was removed.");
        }

        //-----------------------------
        //deletes a company once it has no projects at all
        private async Task DropEmptyCompanyAsync(int companyId)
        {
            //a company with any project left stays
            if ((await _projects.GetByCompanyAsync(companyId)).Count == 0)
            {
                await _companies.DeleteAsync(companyId);
            }
        }

        //-----------------------------
        //removes children first and reports if anything was kept
        private async Task<bool> RemoveNodeAsync(Category node, List<Category> all)
        {
            var kept = false;

            //children go first so the parent can follow
            foreach (var child in all.Where(c => c.ParentCategoryId == node.CategoryId).ToList())
            {
                //a kept child keeps its parent too
                if (await RemoveNodeAsync(child, all))
                {
                    kept = true;
                }
            }

            //used or still a parent means hide not delete
            if (kept || await _tasks.AnyForCategoryAsync(node.CategoryId))
            {
                node.IsArchived = true;
                await _categories.UpdateAsync(node);
                return true;
            }

            await _categories.DeleteAsync(node.CategoryId);
            return false;
        }
    }
}
//------------------------------EOF-----------------------------\\
