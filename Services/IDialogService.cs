namespace MarkdownVault.Services;

public enum ConfirmResult { Yes, No, Cancel }

/// <summary>VM-facing seam for modal UI. Keeps WPF dialogs out of ViewModels so they
/// can be exercised headlessly. Implemented by WpfDialogService; faked in tests.</summary>
public interface IDialogService
{
    // CloseTab :276-282
    ConfirmResult ConfirmSaveChanges(string fileName);

    // SaveAsync :428, SaveAsAsync :456, InsertImage :618, HandleDroppedFiles :652
    void ShowError(string message, string title);

    // InsertInternalLink :584 ("Vault vacío")
    void ShowInfo(string message, string title);

    // SaveAsAsync :436-443 — returns null when the user cancels
    string? AskSaveFilePath(string suggestedFileName, string filter, string defaultExt);

    // InsertImage :602-607
    string? AskImagePath();

    // InsertInternalLink :590-596 — returns the markdown the picker produced (dlg.ResultMarkdown)
    string? PickInternalLinkMarkdown(IReadOnlyList<string> vaultFiles, string currentFilePath, string vaultRoot);
}
