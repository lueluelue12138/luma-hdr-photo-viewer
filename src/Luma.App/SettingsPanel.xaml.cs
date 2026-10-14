using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;

// Constructed on the first settings navigation. The gallery's initial layout
// need not create templates/text/buttons belonging to an invisible page.
public sealed partial class SettingsPanel : UserControl
{
    public SettingsPanel()=>InitializeComponent();
    internal int SelectedThemeIndex {get=>ThemeBox.SelectedIndex;set=>ThemeBox.SelectedIndex=value;}
    internal event SelectionChangedEventHandler? ThemeSelectionChanged;
    internal event RoutedEventHandler? DefaultAppsRequested;
    internal event RoutedEventHandler? DataFolderRequested;
    private void ThemeChanged(object sender,SelectionChangedEventArgs e)=>ThemeSelectionChanged?.Invoke(sender,e);
    private void DefaultAppsClick(object sender,RoutedEventArgs e)=>DefaultAppsRequested?.Invoke(sender,e);
    private void DataFolderClick(object sender,RoutedEventArgs e)=>DataFolderRequested?.Invoke(sender,e);
}
