namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //the shapes the api returns, entities are never exposed directly so the schema can change without breaking clients
    public record CompanyDto(int Id, string Name);

    public record ProjectDto(int Id, string Name, int CompanyId, string CompanyName, string Status, string? Colour);

    public record CategoryDto(int Id, string Name, int? ParentId, string Colour, int SortOrder, bool IsBillable);

    public record TaskDto(int Id, string Name, int ProjectId, int CategoryId, string Status);

    public record TimeEntryDto(int Id, int UserId, int TaskId, int ProjectId, string ProjectName,
        DateTime StartTime, DateTime EndTime, double DurationMinutes, string? Note, string Method);
}
//------------------------------EOF-----------------------------\\
