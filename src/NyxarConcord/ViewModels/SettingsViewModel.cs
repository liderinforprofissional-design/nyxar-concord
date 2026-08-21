using NyxarConcord.Models;
using NyxarConcord.Services;

namespace NyxarConcord.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IdentityService _identityService;
    private readonly AudioDeviceService _audioService;
    private readonly UserIdentity _identity;

    public IReadOnlyList<AudioDevice> InputDevices { get; }
    public IReadOnlyList<AudioDevice> OutputDevices { get; }

    private AudioDevice? _selectedInput;
    public AudioDevice? SelectedInput
    {
        get => _selectedInput;
        set => SetProperty(ref _selectedInput, value);
    }

    private AudioDevice? _selectedOutput;
    public AudioDevice? SelectedOutput
    {
        get => _selectedOutput;
        set => SetProperty(ref _selectedOutput, value);
    }

    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private string _avatarPath;
    public string AvatarPath
    {
        get => _avatarPath;
        set { if (SetProperty(ref _avatarPath, value)) OnPropertyChanged(nameof(Initials)); }
    }

    private bool _noiseSuppression;
    public bool NoiseSuppression
    {
        get => _noiseSuppression;
        set => SetProperty(ref _noiseSuppression, value);
    }

    private bool _soundsEnabled;
    public bool SoundsEnabled
    {
        get => _soundsEnabled;
        set => SetProperty(ref _soundsEnabled, value);
    }

    public string Initials => MainViewModel.Initials(_displayName);
    public string PeerId => _identity.PeerId;

    public SettingsViewModel(UserIdentity identity, IdentityService identityService, AudioDeviceService audioService)
    {
        _identity = identity;
        _identityService = identityService;
        _audioService = audioService;

        _displayName = identity.DisplayName;
        _avatarPath = identity.AvatarPath;
        _noiseSuppression = identity.Audio.NoiseSuppression;
        _soundsEnabled = identity.SoundsEnabled;

        InputDevices = audioService.GetInputDevices();
        OutputDevices = audioService.GetOutputDevices();

        _selectedInput = InputDevices.FirstOrDefault(d => d.Id == identity.Audio.InputDeviceId)
                         ?? InputDevices.FirstOrDefault(d => d.IsDefault)
                         ?? InputDevices.FirstOrDefault();
        _selectedOutput = OutputDevices.FirstOrDefault(d => d.Id == identity.Audio.OutputDeviceId)
                          ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
                          ?? OutputDevices.FirstOrDefault();
    }

    public void Save()
    {
        _identity.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? _identity.DisplayName : DisplayName.Trim();
        _identity.AvatarPath = AvatarPath ?? "";
        _identity.Audio.InputDeviceId = SelectedInput?.Id ?? "";
        _identity.Audio.OutputDeviceId = SelectedOutput?.Id ?? "";
        _identity.Audio.NoiseSuppression = NoiseSuppression;
        _identity.SoundsEnabled = SoundsEnabled;
        _identityService.Save(_identity);
    }
}
