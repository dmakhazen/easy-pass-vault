using System.Windows;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// The "Heading", "Separator" and "?" buttons above the text. They edit the text box like typing does,
// so the draft, the unsaved mark and Ctrl+Z work as for any other change.
public partial class MainWindow
{
    private TextMarkupView? markupView;

    private void HeadingClick(object sender, RoutedEventArgs e) =>
        ApplyTextEdit(TextMarkup.ToggleHeading(TextField.Text, TextField.CaretIndex));

    private void DividerClick(object sender, RoutedEventArgs e) =>
        ApplyTextEdit(TextMarkup.InsertSeparator(TextField.Text, TextField.CaretIndex));

    private void MarkupHelpClick(object sender, RoutedEventArgs e) => MarkupHelp.IsOpen = !MarkupHelp.IsOpen;

    private void ApplyTextEdit(TextEdit edit)
    {
        if (busy || draft is null) return;
        // One undo step: Ctrl+Z removes the inserted or restores the removed characters.
        TextField.BeginChange();
        try
        {
            TextField.Select(edit.Start, edit.Length);
            TextField.SelectedText = edit.Insert;
        }
        finally { TextField.EndChange(); }
        TextField.Select(edit.Caret, 0);
        TextField.Focus();
    }
}
