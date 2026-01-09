// File: Mobile/NewwaysAdmin.Mobile/Components/NavigationHeader.xaml.cs
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.Components
{
    public partial class NavigationHeader : ContentView
    {
        private bool _isMenuExpanded = false;

        // ===== BINDABLE PROPERTIES =====

        public static readonly BindableProperty TitleProperty =
            BindableProperty.Create(nameof(Title), typeof(string), typeof(NavigationHeader), "NewwaysAdmin",
                propertyChanged: (bindable, oldValue, newValue) =>
                {
                    if (bindable is NavigationHeader header)
                        header.TitleLabel.Text = (string)newValue;
                });

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        // ===== CONSTRUCTOR =====

        public NavigationHeader()
        {
            InitializeComponent();
            BuildMenu();
            SetupConnectionMonitor();
        }

        // ===== CONNECTION MONITORING =====

        private void SetupConnectionMonitor()
        {
            var connectionState = ConnectionState.Current;
            if (connectionState != null)
            {
                // Set initial state
                UpdateConnectionDot(connectionState.IsOnline);

                // Subscribe to changes
                connectionState.OnConnectionChanged += OnConnectionStateChanged;
            }
        }

        private void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(() => UpdateConnectionDot(isOnline));
        }

        private void UpdateConnectionDot(bool isOnline)
        {
            ConnectionDot.Color = isOnline ? Colors.LightGreen : Colors.Gray;
        }

        // ===== MENU BUILDING =====

        private void BuildMenu()
        {
            MenuStack.Children.Clear();

            var session = MobileSessionState.Current;

            // Categories - only if user has accounting permission
            if (session?.HasPermission("accounting") == true)
            {
                MenuStack.Children.Add(CreateMenuButton("📂  Categories", OnCategoriesClicked));
            }

            // Edit Categories - only if user has accounting permission
            if (session?.HasPermission("accounting") == true)
            {
                MenuStack.Children.Add(CreateMenuButton("✏️  Edit Categories", OnEditCategoriesClicked));
            }
            // Review Bank Slips - only if user has accounting permission
            if (session?.HasPermission("accounting") == true)
            {
                MenuStack.Children.Add(CreateMenuButton("📋  Review Bank Slips", OnReviewBankSlipsClicked));
            }

            // Settings - always visible
            MenuStack.Children.Add(CreateMenuButton("⚙️  Settings", OnSettingsClicked, "#5B4A99"));
        }

        private Button CreateMenuButton(string text, EventHandler clicked, string bgColor = "#7C3AED")
        {
            var button = new Button
            {
                Text = text,
                BackgroundColor = Color.FromArgb(bgColor),
                TextColor = Colors.White,
                FontSize = 14,
                CornerRadius = 8,
                Padding = new Thickness(15, 12),
                HorizontalOptions = LayoutOptions.Fill
            };
            button.Clicked += clicked;
            return button;
        }

        // ===== EVENT HANDLERS =====

        private void OnHeaderTapped(object sender, EventArgs e)
        {
            _isMenuExpanded = !_isMenuExpanded;
            MenuFrame.IsVisible = _isMenuExpanded;
        }

        private async void OnCategoriesClicked(object sender, EventArgs e)
        {
            CollapseMenu();
            await Shell.Current.GoToAsync("//CategoryBrowserPage");
        }

        private async void OnEditCategoriesClicked(object sender, EventArgs e)
        {
            CollapseMenu();
            await Shell.Current.GoToAsync("CategoryManagementPage");
        }

        private async void OnReviewBankSlipsClicked(object sender, EventArgs e)
        {
            CollapseMenu();
            await Shell.Current.GoToAsync("bankSlipReview");
        }

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            CollapseMenu();
            await Shell.Current.GoToAsync("//SettingsPage");
        }

        private void CollapseMenu()
        {
            _isMenuExpanded = false;
            MenuFrame.IsVisible = false;
        }

        // ===== PUBLIC METHODS =====

        public void RefreshMenu()
        {
            BuildMenu();
        }
    }
}