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
            "trash" => new TrashWidget(),
            _ => null,
        };
        w?.Build();
        return w;
    }

    public static string DisplayName(string kind) => kind switch
    {
        "clock" => "Relógio",
        "calendar" => "Calendário",
        "weather" => "Clima",
        "system" => "Monitor do sistema",
        "battery" => "Bateria",
        "media" => "Mídia",
        "trash" => "Lixeira",
        _ => kind,
    };

    public static string Description(string kind) => kind switch
    {
        "clock" => "Relógio analógico; mostra data e hora ao passar o mouse.",
        "calendar" => "Dia de hoje; clique para ver o mês.",
        "weather" => "Temperatura e condição atual da sua cidade.",
        "system" => "Uso de CPU e memória RAM. Clique abre o Gerenciador de Tarefas.",
        "battery" => "Nível de carga (só aparece em notebooks).",
        "media" => "Música ou vídeo tocando agora, com controles.",
        "trash" => "Abre a Lixeira; arraste arquivos para apagá-los.",
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
        "trash" => "",
        _ => "",
    };
}
