using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

public partial class SettingsWindow
{
    public sealed class FolderRouteDraft
    {
        public string Class {get;set;}
        public string Verb {get;set;}
        public bool Enabled {get;set;}
    }
    public string[] FolderClasses=>FolderRoutes.Classes;
    readonly ObservableCollection<FolderRouteDraft> folderRouteDrafts=new();
#if DEBUG
    internal Action<string> OpenFolderRoutesForVerification;
#endif
    void LoadFolderRoutes()
    {
        try
        {
            var routes=FolderRoutes.Read();
            folderRouteDrafts.Clear();
            foreach(var route in routes)folderRouteDrafts.Add(new(){Class=route.Class,Verb=route.Verb,Enabled=route.Enabled});
            FolderRouteRows.ItemsSource=folderRouteDrafts;
            FolderRoutesStatus.Text="";
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException){FolderRoutesStatus.Text=error.Message;}
    }
    void AddFolderRoute_Click(object sender,RoutedEventArgs e)
    {
        FolderRouteRows.ItemsSource=folderRouteDrafts;
        folderRouteDrafts.Add(new(){Class="Folder",Verb="",Enabled=true});
    }
    void RemoveFolderRoute_Click(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is FolderRouteDraft route)folderRouteDrafts.Remove(route);
    }
    void ApplyFolderRoutes_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            WindowsIntegration.UpdateRoutes(folderRouteDrafts.Select(row=>new FolderRoutes.Route(row.Class,row.Verb,row.Enabled)));
            LoadFolderRoutes();RefreshFolderIntegration();
            FolderRoutesStatus.Text=WindowsIntegration.Enabled?"Routes applied.":"Routes saved. Folder integration is off.";
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException){FolderRoutesStatus.Text=error.Message;RefreshFolderIntegration();}
    }
    void ReloadFolderRoutes_Click(object sender,RoutedEventArgs e)=>LoadFolderRoutes();
    void OpenFolderRoutesJson_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!File.Exists(FolderRoutes.FilePath))FolderRoutes.Write(FolderRoutes.Defaults());
#if DEBUG
            if(OpenFolderRoutesForVerification!=null){OpenFolderRoutesForVerification(FolderRoutes.FilePath);return;}
#endif
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FolderRoutes.FilePath){UseShellExecute=true});
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){FolderRoutesStatus.Text=error.Message;}
    }
}
