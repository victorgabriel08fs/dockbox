using Dockbox.Core;

namespace Dockbox.Widgets;

public static class WidgetFactory
{
    public static DockWidget? Create(string kind)
    {
        DockWidget? w = kind switch
        {
            "clock" => new ClockWidget(),
            "calendar" => new CalendarWidget(),
            "weather" => new WeatherWidget(),
            "system" => new SystemWidget(),
            "battery" => new BatteryWidget(),
            "media" => new MediaWidget(),
            "search" => new SearchWidget(),
            "trash" => new TrashWidget(),
            _ => null,
        };
        w?.Build();
        return w;
    }

    public static string DisplayName(string kind) => kind switch
    {
        "clock" => Loc.T("W_Clock_Name"),
        "calendar" => Loc.T("W_Calendar_Name"),
        "weather" => Loc.T("W_Weather_Name"),
        "system" => Loc.T("W_System_Name"),
        "battery" => Loc.T("W_Battery_Name"),
        "media" => Loc.T("W_Media_Name"),
        "search" => Loc.T("W_Search_Name"),
        "trash" => Loc.T("W_Trash_Name"),
        _ => kind,
    };

    public static string Description(string kind) => kind switch
    {
        "clock" => Loc.T("W_Clock_Desc"),
        "calendar" => Loc.T("W_Calendar_Desc"),
        "weather" => Loc.T("W_Weather_Desc"),
        "system" => Loc.T("W_System_Desc"),
        "battery" => Loc.T("W_Battery_Desc"),
        "media" => Loc.T("W_Media_Desc"),
        "search" => Loc.T("W_Search_Desc"),
        "trash" => Loc.T("W_Trash_Desc"),
        _ => "",
    };

    /// <summary>Glifo (Segoe Fluent/MDL2) usado na tela de gerenciamento.</summary>
    public static string Glyph(string kind) => kind switch
    {
        "clock" => "",
        "calendar" => "",
        "weather" => "",
        "system" => "",
        "battery" => "",
        "media" => "",
        "search" => "\uE721",
        "trash" => "",
        _ => "",
    };
}
