using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace LinkDesk;

public partial class MainWindow : Window
{
    private NativeWifi? wifi;
    private IReadOnlyList<WifiNetwork> discovered = [];
    private CancellationTokenSource? operation;
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ScanAsync();
        Closing += (_, e) =>
        {
            if (operation != null)
            {
                closing = true;
                e.Cancel = true;
                operation.Cancel();
            }
            else { wifi?.Dispose(); Password.Clear(); }
        };
    }

    private async void ScanClicked(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (operation != null) return;
        BeginOperation(false);
        discovered = [];
        ApplyFilter();
        SetStatus("Looking for your phone…", "Waiting for Windows to finish a fresh Wi-Fi scan.");
        try
        {
            wifi ??= new NativeWifi();
            discovered = await wifi.ScanAsync(operation!.Token);
            ApplyFilter();
            SetStatus("Scan complete", Networks.Items.Count == 0
                ? "No matching phone hotspots found. Turn on PdaNet's Wi-Fi Direct hotspot, or show other Wi-Fi networks."
                : $"Found {Networks.Items.Count} matching network(s). Select your phone to connect.");
        }
        catch (OperationCanceledException) { SetStatus("Scan canceled", "You can scan again when ready."); }
        catch (Exception ex) { SetStatus("Couldn’t scan", ex.Message); }
        finally { EndOperation(); }
    }

    private async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (operation != null || Networks.SelectedItem is not WifiNetwork network || wifi == null) return;
        // Validate before switching adapters or entering a pending state.
        try { _ = WifiProfile.Create(network, Password.Password); }
        catch (Exception ex) { SetStatus("Check connection details", ex.Message); return; }
        BeginOperation(true);
        SetStatus("Connecting to phone…", "Waiting up to 30 seconds for Windows to confirm association. You can cancel this attempt.");
        try
        {
            await wifi.ConnectAsync(network, Password.Password, operation!.Token);
            SetStatus("Association confirmed", "Windows confirmed the selected SSID during this attempt. This preview does not continuously monitor the link or implement internet forwarding; tethering is not verified.");
        }
        catch (OperationCanceledException) { SetStatus("Connection canceled", "The pending Wi-Fi request has been stopped. Your previous Wi-Fi connection is not automatically restored."); }
        catch (Exception ex) { SetStatus("Couldn’t connect", ex.Message); }
        finally { Password.Clear(); EndOperation(); }
    }

    private void BeginOperation(bool connecting)
    {
        operation = new CancellationTokenSource();
        ScanButton.IsEnabled = ConnectButton.IsEnabled = ShowAll.IsEnabled = Networks.IsEnabled = Password.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        CancelButton.Visibility = connecting ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EndOperation()
    {
        operation?.Dispose();
        operation = null;
        ScanButton.IsEnabled = ShowAll.IsEnabled = Networks.IsEnabled = Password.IsEnabled = true;
        ConnectButton.IsEnabled = Networks.SelectedItem is WifiNetwork;
        Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
        if (closing) Close();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void LocationClicked(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("Open Windows Settings", ex.Message); }
    }
    private void NetworkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Networks.SelectedItem is WifiNetwork network)
        {
            SelectedName.Text = network.Name;
            ConnectButton.IsEnabled = operation == null;
            Password.Clear();
        }
        else { SelectedName.Text = "Choose your phone"; ConnectButton.IsEnabled = false; }
    }
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) ApplyFilter();
    }
    private void ApplyFilter()
    {
        string? selection = (Networks.SelectedItem as WifiNetwork)?.Identity;
        Networks.ItemsSource = discovered.Where(n => ShowAll.IsChecked == true || n.IsPhone).ToArray();
        Networks.SelectedItem = Networks.Items.Cast<WifiNetwork>().FirstOrDefault(n => n.Identity == selection);
        EmptyHint.Visibility = Networks.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetStatus(string title, string detail) { StatusTitle.Text = title; StatusDetail.Text = detail; }
}
