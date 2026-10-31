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
        //renames a client, its projects and logged time follow the new name
        public async Task<ListChangeOutcome> RenameCompanyAsync(int companyId, string name)
        {
            var clean = name?.Trim() ?? string.Empty;
            var all = await _companies.GetAllAsync();
            var company = all.FirstOrDefault(c => c.CompanyId == companyId);

            //a missing client can't be renamed
            if (company == null)
            {
                return ListChangeOutcome.Refused("That client no longer exists.");
            }

            //internal work keeps its name so billing stays right
            if (BillingRules.IsInternal(company.Name) || BillingRules.IsInternal(clean))
            {
                return ListChangeOutcome.Refused("Internal can't be renamed.");
            }

            //names follow the import rules so a day can be sent later
            if (!NameRules.IsValidCompanyOrProjectName(clean))
            {
                return ListChangeOutcome.Refused("Names must be 1 to 100 characters and cannot start with = + - or @.");
            }

            //two clients with one name would merge on the dashboard
            if (all.Any(c => c.CompanyId != companyId && string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            {
                return ListChangeOutcome.Refused($"There is already a client called {clean}.");
            }

            company.Name = clean;
            await _companies.UpdateAsync(company);
            return ListChangeOutcome.Done($"Renamed to {clean}.");
        }

        //-----------------------------
        //renames a project, logged time follows the new name
        public async Task<ListChangeOutcome> RenameProjectAsync(int projectId, string name)
        {
            var clean = name?.Trim() ?? string.Empty;
            var project = await _projects.GetByIdAsync(projectId);

            //a missing project can't be renamed
            if (project == null)
            {
                return ListChangeOutcome.Refused("That project no longer exists.");
            }

            //the internal project is set up on every PC and keeps its name
            if (IsInternalProject(project))
            {
                return ListChangeOutcome.Refused("Internal can't be renamed.");
            }

            //names follow the import rules so a day can be sent later
            if (!NameRules.IsValidCompanyOrProjectName(clean))
            {
                return ListChangeOutcome.Refused("Names must be 1 to 100 characters and cannot start with = + - or @.");
            }

            //two projects with one name in a client would merge on the dashboard
            if ((await _projects.GetByCompanyAsync(project.CompanyId))
                .Any(p => p.ProjectID != projectId && string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
            {
                return ListChangeOutcome.Refused($"{project.Company?.Name} already has a project called {clean}.");
            }

            project.Name = clean;
            await _projects.UpdateAsync(project);
            return ListChangeOutcome.Done($"Renamed to {clean}.");
        }

        //-----------------------------
        //renames a sub activity, logged time follows the new name
        public async Task<ListChangeOutcome> RenameActivityAsync(int categoryId, string name)
        {
            var clean = name?.Trim() ?? string.Empty;
            var all = await _categories.GetAllAsync();
            var target = all.FirstOrDefault(c => c.CategoryId == categoryId);

            //a missing activity can't be renamed
            if (target == null)
            {
                return ListChangeOutcome.Refused("That activity no longer exists.");
            }

            //top level activities are shared and keep their names
            if (target.ParentCategoryId == null)
            {
                return ListChangeOutcome.Refused("Main activities can't be renamed.");
            }

            //names follow the shared rules
            if (!NameRules.IsValidActivityName(clean))
            {
                return ListChangeOutcome.Refused("Activity names must be 1 to 60 characters without › or >.");
            }

            //two siblings with one name would be one path
            if (all.Any(c => c.ParentCategoryId == target.ParentCategoryId && c.CategoryId != categoryId &&
                             string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            {
                return ListChangeOutcome.Refused($"There is already an activity called {clean} here.");
            }

            target.Name = clean;
            await _categories.UpdateAsync(target);
            return ListChangeOutcome.Done($"Renamed to {clean}.");
        }

        //-----------------------------
        //hides every project of a client and keeps their logged time
        public Task<ListChangeOutcome> HideCompanyAsync(int companyId) => SetCompanyHiddenAsync(companyId, true);

        //-----------------------------
        //brings back every project of a hidden client
        public Task<ListChangeOutcome> ShowCompanyAsync(int companyId) => SetCompanyHiddenAsync(companyId, false);

        //-----------------------------
        //hides a project and keeps its logged time
        public Task<ListChangeOutcome> HideProjectAsync(int projectId) => SetProjectHiddenAsync(projectId, true);

        //-----------------------------
        //brings back a hidden project
        public Task<ListChangeOutcome> ShowProjectAsync(int projectId) => SetProjectHiddenAsync(projectId, false);

        //-----------------------------
        //hides a sub activity and everything under it, logged time is kept
        public async Task<ListChangeOutcome> HideActivityAsync(int categoryId)
        {
            var target = await _categories.GetByIdAsync(categoryId);

            //a missing activity can't be hidden
            if (target == null)
            {
                return ListChangeOutcome.Refused("That activity no longer exists.");
            }

            //top level activities are shared and always stay
            if (target.ParentCategoryId == null)
            {
                return ListChangeOutcome.Refused("Main activities can't be hidden.");
            }

            target.IsArchived = true;
            await _categories.UpdateAsync(target);
            return ListChangeOutcome.Done($"{target.Name} is hidden.");
        }

        //-----------------------------
        //brings back a hidden activity and any hidden parents above it
        public async Task<ListChangeOutcome> ShowActivityAsync(int categoryId)
        {
            var all = await _categories.GetAllAsync();
            var target = all.FirstOrDefault(c => c.CategoryId == categoryId);

            //a missing activity can't be shown
            if (target == null)
            {
                return ListChangeOutcome.Refused("That activity no longer exists.");
            }

            //each hidden level up to the root comes back
            for (var node = target; node != null; node = all.FirstOrDefault(c => c.CategoryId == node.ParentCategoryId))
            {
                if (node.IsArchived)
                {
                    node.IsArchived = false;
                    await _categories.UpdateAsync(node);
                }
            }

            return ListChangeOutcome.Done($"{target.Name} is back.");
        }

        //-----------------------------
        //closes or reopens every project of a client
        private async Task<ListChangeOutcome> SetCompanyHiddenAsync(int companyId, bool hidden)
        {
            var company = await _companies.GetByIdAsync(companyId);

            //a missing client can't change
            if (company == null)
            {
                return ListChangeOutcome.Refused("That client no longer exists.");
            }

            //internal work always stays
            if (BillingRules.IsInternal(company.Name))
            {
                return ListChangeOutcome.Refused("Internal can't be hidden.");
            }

            var status = hidden ? ProjectStatus.Closed : ProjectStatus.Active;

            //each project changes on its own
            foreach (var project in await _projects.GetByCompanyAsync(companyId))
            {
                if (project.Status != status)
                {
                    project.Status = status;
                    await _projects.UpdateAsync(project);
                }
            }

            return ListChangeOutcome.Done(hidden ? $"{company.Name} is hidden." : $"{company.Name} is back.");
        }

        //-----------------------------
        //closes or reopens one project
        private async Task<ListChangeOutcome> SetProjectHiddenAsync(int projectId, bool hidden)
        {
            var project = await _projects.GetByIdAsync(projectId);

            //a missing project can't change
            if (project == null)
            {
                return ListChangeOutcome.Refused("That project no longer exists.");
            }

            //the internal project always stays
            if (IsInternalProject(project))
            {
                return ListChangeOutcome.Refused("Internal can't be hidden.");
            }

            project.Status = hidden ? ProjectStatus.Closed : ProjectStatus.Active;
            await _projects.UpdateAsync(project);
            return ListChangeOutcome.Done(hidden ? $"{project.Name} is hidden." : $"{project.Name} is back.");
        }

        //-----------------------------
        //true for the internal project every PC is set up with
        public static bool IsInternalProject(Project project) =>
            BillingRules.IsInternal(project.Company?.Name) &&
            string.Equals(project.Name, LocalSetupService.InternalProjectName, StringComparison.OrdinalIgnoreCase);

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
