using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class TasksController : ApiControllerBase
    {
        private readonly IWorkTaskRepository _tasks;

        public TasksController(IWorkTaskRepository tasks) => _tasks = tasks;

        //-----------------------------
        //the caller's own tasks that are not Done, optionally for one project. Tasks belong to a user, so this never returns anyone else's
        [HttpGet]
        public async Task<ActionResult<List<TaskDto>>> GetMine([FromQuery] int? projectId)
        {
            if (CurrentAppUserId is not int userId)
                return NoProfile();

            return (await _tasks.GetActiveForUserAsync(userId))
                .Where(t => projectId == null || t.ProjectID == projectId)
                .Select(t => new TaskDto(t.TaskID, t.Name, t.ProjectID, t.CategoryId, t.Status.ToString()))
                .ToList();
        }
    }
}
//------------------------------EOF-----------------------------\\
