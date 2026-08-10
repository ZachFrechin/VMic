using Vmic.App.Services;

namespace Vmic.App.ViewModels;

public enum AppRole { Picker, Host, Client }

/// <summary>Root view model: owns the role picker and the Host/Client view models.</summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;

    public HostViewModel Host { get; }
    public ClientViewModel Client { get; }

    private AppRole _role = AppRole.Picker;
    public AppRole Role
    {
        get => _role;
        private set
        {
            if (Set(ref _role, value))
            {
                OnPropertyChanged(nameof(ShowPicker));
                OnPropertyChanged(nameof(ShowHost));
                OnPropertyChanged(nameof(ShowClient));
            }
        }
    }

    public bool ShowPicker => Role == AppRole.Picker;
    public bool ShowHost => Role == AppRole.Host;
    public bool ShowClient => Role == AppRole.Client;

    public RelayCommand ChooseHostCommand { get; }
    public RelayCommand ChooseClientCommand { get; }
    public RelayCommand BackCommand { get; }

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        Host = new HostViewModel(_settings);
        Client = new ClientViewModel(_settings);

        ChooseHostCommand = new RelayCommand(() => SetRole(AppRole.Host));
        ChooseClientCommand = new RelayCommand(() => SetRole(AppRole.Client));
        BackCommand = new RelayCommand(() => SetRole(AppRole.Picker));

        // Restore the last-used role (show the view; don't auto-start anything).
        Role = _settings.LastRole switch
        {
            "Host" => AppRole.Host,
            "Client" => AppRole.Client,
            _ => AppRole.Picker,
        };
        if (Role == AppRole.Client) Client.Activate();
    }

    private void SetRole(AppRole role)
    {
        if (role == Role) return;

        // Leaving the client view pauses discovery.
        if (Role == AppRole.Client) Client.Deactivate();

        Role = role;
        _settings.LastRole = role == AppRole.Picker ? null : role.ToString();
        _settings.Save();

        if (role == AppRole.Client) Client.Activate();
    }

    public void Dispose()
    {
        Host.Dispose();
        Client.Dispose();
    }
}
