namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //compiler contract for building days
    public interface IDayCompiler
    {
        string Model { get; }

        Task<DayBuildResult> BuildAsync(DayBuildRequest request, CancellationToken ct);

        Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);

        Task<string?> TestAsync(CancellationToken ct);
    }
}
//------------------------------EOF-----------------------------\\
