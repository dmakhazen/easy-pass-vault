namespace EasyPassVault.Core;

public enum ItemKind { Login, Text }
public sealed record FolderInfo(string Id, string Name);
public sealed record VaultItem(string Id, string FolderId, ItemKind Kind, string Title,
    string Login = "", string Password = "", string Site = "", string Text = "")
{
    public bool HasContent => new[] { Title, Login, Password, Site, Text }.Any(s => !string.IsNullOrWhiteSpace(s));
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Kind == ItemKind.Text ? "Новый файл" : "Новая запись" : Title;
}
public sealed class VaultException(string message, Exception? inner = null) : Exception(message, inner);
