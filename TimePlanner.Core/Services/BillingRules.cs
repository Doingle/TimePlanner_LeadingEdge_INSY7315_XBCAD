namespace TimePlanner.Core.Services
{
    //-----------------------------
    //decides if time is billable from company and activity
    public static class BillingRules
    {
        //-----------------------------
        //true for the internal company
        public static bool IsInternal(string? companyName) =>
            string.Equals(companyName?.Trim(), LocalSetupService.InternalCompanyName, StringComparison.OrdinalIgnoreCase);

        //-----------------------------
        //billable unless internal work or a non billable top level activity
        public static bool IsBillable(string? companyName, bool activityRootBillable) =>
            !IsInternal(companyName) && activityRootBillable;
    }
}
//------------------------------EOF-----------------------------\\
