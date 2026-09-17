namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a project which is commisioned by a company (client) and is connected to a task within TimePlanner
    public class Project
    {
        public int ProjectID { get; set; }
        public int? CompanyId { get; set; }
        public Company? Company { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public ICollection<WorkTask> Tasks { get; set; } = new List<WorkTask>();
        public List<WorkTask> GetTasks() => Tasks.ToList();
    }
}
//------------------------------EOF-----------------------------\\
