using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a project which is commisioned by a company (client) and is connected to a task within TimePlanner
    public class Project
    {
        public int ProjectID { get; set; }

        //every project belongs to  one company [internal work] belongs to [internal] company (Ledge)
        public int CompanyId { get; set; }
        public Company? Company { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ProjectStatus Status { get; set; } = ProjectStatus.Active;
        public ICollection<WorkTask> Tasks { get; set; } = new List<WorkTask>();
        public List<WorkTask> GetTasks() => Tasks.ToList();
    }
}
//------------------------------EOF-----------------------------\\
