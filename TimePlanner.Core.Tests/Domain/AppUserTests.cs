using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Tests.Domain
{
    //-----------------------------
    //unit tests for AppUser behaviour that needs no db
    public class AppUserTests
    {
        //-----------------------------
        //done tasks are left out of the active task list while open/ in progress tasks are kept inside
        [Fact]
        public void GetActiveTasks_ExcludesDoneTasks()
        {
            var user = new AppUser();
            user.AssignedTasks.Add(new WorkTask { Name = "open task", Status = WorkTaskStatus.Open });
            user.AssignedTasks.Add(new WorkTask { Name = "busy task", Status = WorkTaskStatus.InProgress });
            user.AssignedTasks.Add(new WorkTask { Name = "finished task", Status = WorkTaskStatus.Done });

            var active = user.GetActiveTasks();

            Assert.Equal(2, active.Count);
            Assert.DoesNotContain(active, t => t.Status == WorkTaskStatus.Done);
        }
    }
}
//------------------------------EOF-----------------------------\\
