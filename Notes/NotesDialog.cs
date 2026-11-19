using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

/// BROWSE-07: Tags, rating and comment… for one or several items. With several, a field left as
/// "(various)" keeps each item's own value.
internal sealed class NotesDialog : AuxDialog
{
    const string Various="(various)";
    readonly TextBox tags,comment;readonly Button[] stars=new Button[5];int rating;bool ratingChanged;
    readonly string[] paths;readonly FileNote[] before;
    NotesDialog(Window owner,string[] paths):base(owner,paths.Length==1?"Tags and notes · "+Path.GetFileName(paths[0].TrimEnd('\\')):$"Tags and notes · {paths.Length:N0} items","Save")
    {
        this.paths=paths;before=paths.Select(FileNotes.Find).ToArray();
        string Same(Func<FileNote,string> value){var all=before.Select(n=>n==null?"":value(n)).Distinct().ToArray();return all.Length==1?all[0]:Various;}
        Body.Children.Add(Label("Tags (separated by commas)",0));
        tags=Field(Same(n=>n.TagText),"Tags",out var tagFrame);Body.Children.Add(tagFrame);
        var known=FileNotes.AllTags();if(known.Length>0)Body.Children.Add(Note("In use: "+string.Join(", ",known.Take(30))));
        Body.Children.Add(Label("Rating"));
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        var ratings=before.Select(n=>n?.Rating??0).Distinct().ToArray();rating=ratings.Length==1?ratings[0]:0;
        for(int i=0;i<5;i++)
        {
            int value=i+1;
            var star=stars[i]=new Button{FontSize=18,Padding=new Thickness(4,0,4,0),MinWidth=0};star.SetResourceReference(StyleProperty,"Ghost");
            System.Windows.Automation.AutomationProperties.SetName(star,$"{value} star{(value>1?"s":"")}");
            star.Click+=(_,_)=>{rating=rating==value?0:value;ratingChanged=true;ShowStars();};row.Children.Add(star);
        }
        Body.Children.Add(row);ShowStars();
        if(ratings.Length>1)Body.Children.Add(Note("The items have different ratings; choosing one sets it for all."));
        Body.Children.Add(Label("Comment"));
        comment=Field(Same(n=>n.Comment),"Comment",out var commentFrame,multiline:true);Body.Children.Add(commentFrame);
        Body.Children.Add(Note("Kept in Pathweft's notes database, not in the files. Follows renames and moves on NTFS and ReFS drives."));
        Loaded+=(_,_)=>{tags.Focus();tags.SelectAll();};
    }
    void ShowStars(){for(int i=0;i<5;i++)stars[i].Content=i<rating?"★":"☆";}
    internal static bool Edit(Window owner,IReadOnlyList<string> paths)
    {
        if(paths.Count==0)return false;
        var dialog=new NotesDialog(owner,paths.ToArray());if(!dialog.Present())return false;
        for(int i=0;i<dialog.paths.Length;i++)
        {
            var old=dialog.before[i]??new FileNote([],0,"");
            var note=new FileNote(dialog.tags.Text==Various?old.Tags:FileNote.SplitTags(dialog.tags.Text),dialog.ratingChanged?dialog.rating:old.Rating,dialog.comment.Text==Various?old.Comment:dialog.comment.Text.Trim());
            if(note!=old)FileNotes.Set(dialog.paths[i],note);
        }
        return true;
    }
}
