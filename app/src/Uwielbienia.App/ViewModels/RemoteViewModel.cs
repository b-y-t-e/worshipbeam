using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uwielbienia.App.Services;
using Uwielbienia.Link;
using Uwielbienia.Core.Localization;

namespace Uwielbienia.App.ViewModels;

/// <summary>
/// Parowanie przez tailcat-link: telefon (pilot, przeglądarka jako drugi ekran) albo panel na drugim komputerze lub
/// tablecie (plany, plan, pieśń, ekran — jak w oknie operatora).
/// </summary>
public sealed partial class RemoteViewModel : ObservableObject
{
    private readonly IRemoteServer _server;

    public RemoteViewModel(IRemoteServer server)
    {
        _server = server;
        _server.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial Bitmap? QrCode { get; private set; }

    [ObservableProperty]
    public partial string? PageUrl { get; private set; }

    /// <summary>Krótki kod do podyktowania (np. „ajk59”); <c>null</c>, gdy usługa nie odpowiedziała.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortCode))]
    public partial string? ShortCode { get; private set; }

    public bool HasShortCode => ShortCode is not null;

    [ObservableProperty]
    public partial string? ExpiresText { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Kod prowadzi do panelu (drugi komputer, tablet) zamiast pilota w telefonie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForPilot), nameof(Hint))]
    public partial bool ForPanel { get; private set; }

    public bool ForPilot => !ForPanel;

    public string Hint => ForPanel
        ? Tr.T("Na drugim komputerze lub tablecie zeskanuj kod albo otwórz adres spod kodu. Otworzy się panel: plany, plan, pieśni i to, co jest na ekranie — wszystko jak tutaj.")
        : Tr.T("Zeskanuj kod aparatem telefonu. Otworzy się pilot: tekst z akordami, Dalej i Wstecz. Na komputerze otwórz ten sam adres i wybierz „Ekran”, żeby pokazywać tekst w przeglądarce.");

    public string StatusText => _server.Status switch
    {
        RemoteServerStatus.Off => Tr.T("Połączenia wyłączone"),
        RemoteServerStatus.Starting => Tr.T("Łączenie z serwerem pośredniczącym…"),
        RemoteServerStatus.Failed => Tr.F("Nie udało się uruchomić połączeń: {0}", _server.LastError),
        _ => _server.ConnectedDevices switch
        {
            0 => Tr.T("Brak połączonych urządzeń"),
            1 => Tr.T("Połączone: 1 urządzenie"),
            var n => Tr.F("Połączone urządzenia: {0}", n),
        },
    };

    public string BadgeText => _server.ConnectedDevices > 0 ? Tr.F("Urządzenia: {0}", _server.ConnectedDevices) : Tr.T("Urządzenia");

    public bool HasDevices => _server.ConnectedDevices > 0;

    /// <summary>Sparowane urządzenia z uprawnieniem „Dodaje pieśni”.</summary>
    public ObservableCollection<RemoteDeviceViewModel> Devices { get; } = [];

    public bool HasPairedDevices => Devices.Count > 0;

    public string? DevicesError => _server.DevicesError;

    /// <summary>Uruchamia połączenia w tle przy starcie — sparowane wcześniej telefony łączą się same.</summary>
    public async Task StartAsync() => await _server.StartAsync();

    [RelayCommand]
    private async Task Show()
    {
        IsOpen = true;
        if (QrCode is null)
            await Invite();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task ShowPilot() => await Switch(panel: false);

    [RelayCommand]
    private async Task ShowPanel() => await Switch(panel: true);

    private async Task Switch(bool panel)
    {
        if (ForPanel == panel && QrCode is not null)
            return;
        ForPanel = panel;
        QrCode = null;
        ShortCode = null;
        PageUrl = null;
        ExpiresText = null;
        await Invite();
    }

    [RelayCommand]
    private async Task Invite()
    {
        IsBusy = true;
        try
        {
            await _server.StartAsync();
            if (_server.Status != RemoteServerStatus.Ready)
                return;
            var invitation = await _server.InviteAsync(ForPanel ? RemoteClient.Panel : RemoteClient.Pilot);
            // kod QR z długim kodem (działa bez usługi krótkich kodów); do dyktowania — adres z krótkim
            QrCode = QrCodeRenderer.Render(invitation.PageUrl.ToString());
            ShortCode = invitation.ShortCode;
            PageUrl = (invitation.ShortPageUrl ?? invitation.PageUrl).ToString();
            ExpiresText = invitation.ShortExpiresAt is { } shortExpires
                ? Tr.F("Kod ważny do {0:HH:mm} (kod QR do {1:HH:mm}) i tylko dla jednego urządzenia.", shortExpires.ToLocalTime(), invitation.ExpiresAt.ToLocalTime())
                : Tr.F("Kod ważny do {0:HH:mm} i tylko dla jednego urządzenia.", invitation.ExpiresAt.ToLocalTime());
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        Devices.Clear();
        foreach (var device in _server.Devices)
            Devices.Add(new RemoteDeviceViewModel(device, _server));
        OnPropertyChanged(nameof(HasPairedDevices));
        OnPropertyChanged(nameof(DevicesError));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasDevices));
    }
}

/// <summary>
/// Wiersz urządzenia w oknie „Urządzenia”; przełącznik od razu zmienia uprawnienie (telefon pokazuje albo chowa „+”).
/// „Zapomnij” pyta raz („Zapomnieć?”), drugie kliknięcie rozparowuje.
/// </summary>
public sealed partial class RemoteDeviceViewModel(RemoteDevice device, IRemoteDeviceManager server) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsConfirmingForget { get; private set; }

    [RelayCommand]
    private async Task Forget()
    {
        if (!IsConfirmingForget)
        {
            IsConfirmingForget = true;
            return;
        }
        await server.ForgetAsync(device.Key);
    }

    public string Name => device.Name;

    public bool IsConnected => device.IsConnected;

    public string Details => (device.IsConnected ? Tr.T("połączone") : Tr.T("niepołączone")) +
        Tr.F(" · sparowane {0:d MMM}", device.PairedAt.ToLocalTime());

    public bool CanAddSongs
    {
        get => device.CanAddSongs;
        set => server.SetPermission(device.Key, DevicePermission.AddSongs, value);
    }

    /// <summary>Panel: pełne sterowanie planami i planem (przy panelu „Dodaje pieśni” nie ma znaczenia).</summary>
    public bool IsPanel
    {
        get => device.IsPanel;
        set => server.SetPermission(device.Key, DevicePermission.Panel, value);
    }

    public bool IsPilot => !device.IsPanel;
}
