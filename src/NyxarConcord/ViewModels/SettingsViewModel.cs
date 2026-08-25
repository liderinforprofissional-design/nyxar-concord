using NAudio.Wave;
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

    // ---------------- Teste de microfone (medidor de nível ao vivo) ----------------
    private WaveInEvent? _testMic;

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        private set { if (SetProperty(ref _isTesting, value)) OnPropertyChanged(nameof(TestButtonLabel)); }
    }

    public string TestButtonLabel => _isTesting ? "Parar teste" : "Testar microfone";

    private double _micLevel;
    /// <summary>Nível do microfone (0 a 1) para a barra do medidor.</summary>
    public double MicLevel { get => _micLevel; private set => SetProperty(ref _micLevel, value); }

    public void ToggleMicTest()
    {
        if (_isTesting) { StopMicTest(); return; }
        try
        {
            int dev = int.TryParse(SelectedInput?.Id, out var n) ? n : -1;
            _testMic = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
                DeviceNumber = dev
            };
            _testMic.DataAvailable += OnTestData;
            _testMic.StartRecording();
            IsTesting = true;
        }
        catch { StopMicTest(); }
    }

    private void OnTestData(object? sender, WaveInEventArgs e)
    {
        double rms = Rms(e.Buffer, e.BytesRecorded);
        double level = Math.Clamp(rms / 3000.0, 0, 1); // ~fala normal chega perto de 1
        var app = System.Windows.Application.Current;
        if (app is not null) app.Dispatcher.BeginInvoke(() => MicLevel = level);
        else MicLevel = level;
    }

    public void StopMicTest()
    {
        try
        {
            if (_testMic is not null)
            {
                _testMic.DataAvailable -= OnTestData;
                _testMic.StopRecording();
                _testMic.Dispose();
            }
        }
        catch { }
        _testMic = null;
        IsTesting = false;
        MicLevel = 0;
    }

    private static double Rms(byte[] buf, int count)
    {
        int samples = count / 2;
        if (samples == 0) return 0;
        long sum = 0;
        for (int i = 0; i + 1 < count; i += 2)
        {
            short v = (short)(buf[i] | (buf[i + 1] << 8));
            sum += (long)v * v;
        }
        return Math.Sqrt((double)sum / samples);
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
