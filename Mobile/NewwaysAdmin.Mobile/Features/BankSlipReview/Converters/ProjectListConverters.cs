// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/Converters/ProjectListConverters.cs
// Value converters for the project list page

using System.Globalization;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.Converters;

/// <summary>
/// Converts selected filter/sort value to button background color.
/// Returns primary color if selected, gray if not.
/// Usage: BackgroundColor="{Binding SelectedFilter, Converter={StaticResource FilterButtonColorConverter}, ConverterParameter=All}"
/// </summary>
public class FilterButtonColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value?.ToString();
        var buttonValue = parameter?.ToString();

        if (string.Equals(selected, buttonValue, StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb("#2196F3"); // Primary blue - selected
        }

        return Color.FromArgb("#9E9E9E"); // Gray - not selected
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to color.
/// True = normal text color, False = warning color.
/// Usage: TextColor="{Binding HasStructuralNote, Converter={StaticResource BoolToColorConverter}}"
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue
                ? Color.FromArgb("#333333")  // Normal - has category
                : Color.FromArgb("#FF9800"); // Orange - needs attention
        }

        return Color.FromArgb("#333333");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null/empty string to boolean.
/// Returns true if value is NOT null/empty, false otherwise.
/// Usage: IsVisible="{Binding LocationName, Converter={StaticResource NullToBoolConverter}}"
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strValue)
        {
            return !string.IsNullOrEmpty(strValue);
        }

        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// Usage: IsVisible="{Binding IsBusy, Converter={StaticResource InverseBoolConverter}}"
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}