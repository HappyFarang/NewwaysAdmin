// File: NewwaysAdmin.WebAdmin/Services/Workers/ColumnDefinitionService.cs
// Purpose: Central definition of all available table columns

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public interface IColumnDefinitionService
    {
        List<TableColumn> GetAllColumns();
        List<TableColumn> GetDefaultVisibleColumns();
        List<string> GetCategories();
        List<TableColumn> GetColumnsByCategory(string category);
        TableColumn? GetColumnByKey(string key);
    }

    public class ColumnDefinitionService : IColumnDefinitionService
    {
        private readonly List<TableColumn> _allColumns;

        public ColumnDefinitionService()
        {
            _allColumns = DefineAllColumns();
        }

        public List<TableColumn> GetAllColumns() => _allColumns;

        public List<TableColumn> GetDefaultVisibleColumns()
            => _allColumns.Where(c => c.DefaultVisible).OrderBy(c => c.SortOrder).ToList();

        public List<string> GetCategories()
            => _allColumns.Select(c => c.Category).Distinct().ToList();

        public List<TableColumn> GetColumnsByCategory(string category)
            => _allColumns.Where(c => c.Category == category).OrderBy(c => c.SortOrder).ToList();

        public TableColumn? GetColumnByKey(string key)
            => _allColumns.FirstOrDefault(c => c.Key == key);

        private List<TableColumn> DefineAllColumns()
        {
            return new List<TableColumn>
            {
                // 📅 Basic Info Category
                new()
                {
                    Key = "dayName",
                    DisplayName = "Day",
                    Category = "Basic Info",
                    DefaultVisible = true,
                    SortOrder = 1,
                    DataSource = ColumnDataSource.Display,
                    IsAdjustable = false,
                    Icon = "bi-calendar-day",
                    Description = "Day of the week"
                },
                new()
                {
                    Key = "date",
                    DisplayName = "Date",
                    Category = "Basic Info",
                    DefaultVisible = true,
                    SortOrder = 2,
                    DataSource = ColumnDataSource.Display,
                    IsAdjustable = false,
                    Icon = "bi-calendar",
                    Description = "Date (MMM dd format)"
                },

                // 🕐 Normal Times Category
                new()
                {
                    Key = "signIn",
                    DisplayName = "Sign In",
                    Category = "Normal Times",
                    DefaultVisible = true,
                    SortOrder = 10,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-clock",
                    Description = "Normal shift sign-in time"
                },
                new()
                {
                    Key = "signOut",
                    DisplayName = "Sign Out",
                    Category = "Normal Times",
                    DefaultVisible = true,
                    SortOrder = 11,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-clock-history",
                    Description = "Normal shift sign-out time"
                },

                // ⏰ OT Times Category
                new()
                {
                    Key = "otSignIn",
                    DisplayName = "OT Sign In",
                    Category = "OT Times",
                    DefaultVisible = false,
                    SortOrder = 20,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-clock-fill",
                    Description = "Overtime sign-in time"
                },
                new()
                {
                    Key = "otSignOut",
                    DisplayName = "OT Sign Out",
                    Category = "OT Times",
                    DefaultVisible = false,
                    SortOrder = 21,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-clock-fill",
                    Description = "Overtime sign-out time"
                },

                // 📊 Hours Category
                new()
                {
                    Key = "normalHours",
                    DisplayName = "Normal Hours",
                    Category = "Hours",
                    DefaultVisible = true,
                    SortOrder = 30,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-hourglass-split",
                    Description = "Normal working hours"
                },
                new()
                {
                    Key = "otHours",
                    DisplayName = "OT Hours",
                    Category = "Hours",
                    DefaultVisible = true,
                    SortOrder = 31,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-hourglass-top",
                    Description = "Overtime hours"
                },
                new()
                {
                    Key = "totalHours",
                    DisplayName = "Total Hours",
                    Category = "Hours",
                    DefaultVisible = true,
                    SortOrder = 32,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-hourglass",
                    Description = "Total working hours (Normal + OT)"
                },

                // 📈 Analysis Category
                new()
                {
                    Key = "variance",
                    DisplayName = "Variance",
                    Category = "Analysis",
                    DefaultVisible = false,
                    SortOrder = 40,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-graph-up-arrow",
                    Description = "Difference from expected hours"
                },
                new()
                {
                    Key = "efficiency",
                    DisplayName = "Efficiency",
                    Category = "Analysis",
                    DefaultVisible = false,
                    SortOrder = 41,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-speedometer2",
                    Description = "Work efficiency percentage"
                },

                // ✅ Status Category
                new()
                {
                    Key = "onTimeStatus",
                    DisplayName = "On Time",
                    Category = "Status",
                    DefaultVisible = true,
                    SortOrder = 50,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = true,
                    Icon = "bi-check-circle",
                    Description = "On-time arrival status"
                },
                /*
                new()
                {
                    Key = "adjustmentStatus",
                    DisplayName = "Adjustments",
                    Category = "Status",
                    DefaultVisible = false,
                    SortOrder = 51,
                    DataSource = ColumnDataSource.DirectService,
                    IsAdjustable = false,
                    Icon = "bi-gear",
                    Description = "Shows if data has been adjusted"
                },
                */
                // 💰 Pay Category
                new()
                {
                    Key = "dailyPay",
                    DisplayName = "Daily Pay",
                    Category = "Pay",
                    DefaultVisible = false,
                    SortOrder = 60,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-cash",
                    Description = "Base daily salary"
                },
                new()
                {
                    Key = "otPay",
                    DisplayName = "OT Pay",
                    Category = "Pay",
                    DefaultVisible = false,
                    SortOrder = 61,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-cash-stack",
                    Description = "Overtime pay"
                },
                new()
                {
                    Key = "totalPay",
                    DisplayName = "Total Pay",
                    Category = "Pay",
                    DefaultVisible = false,
                    SortOrder = 62,
                    DataSource = ColumnDataSource.Calculated,
                    IsAdjustable = false,
                    Icon = "bi-currency-dollar",
                    Description = "Total daily pay (Base + OT)"
                }

                // Easy to add new columns here:
                // new()
                // {
                //     Key = "newMetric",
                //     DisplayName = "New Metric",
                //     Category = "Analysis", 
                //     DefaultVisible = false,
                //     SortOrder = 42,
                //     DataSource = ColumnDataSource.Calculated,
                //     IsAdjustable = false,
                //     Icon = "bi-graph-up",
                //     Description = "Description of new metric"
                // }
            };
        }
    }
}