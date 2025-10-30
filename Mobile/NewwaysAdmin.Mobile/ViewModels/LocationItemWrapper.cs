// File: Mobile/NewwaysAdmin.Mobile/ViewModels/LocationItemWrapper.cs
using CommunityToolkit.Mvvm.ComponentModel;
using NewwaysAdmin.SharedModels.Categories;

namespace NewwaysAdmin.Mobile.ViewModels
{
    /// <summary>
    /// Wrapper for MobileLocationItem with selection state
    /// </summary>
    public partial class LocationItemWrapper : ObservableObject
    {
        public MobileLocationItem Location { get; }

        [ObservableProperty]
        private bool isSelected;

        public LocationItemWrapper(MobileLocationItem location, bool isSelected = false)
        {
            Location = location;
            IsSelected = isSelected;
        }

        public string Id => Location.Id;
        public string Name => Location.Name;
        public int SortOrder => Location.SortOrder;
    }
}
