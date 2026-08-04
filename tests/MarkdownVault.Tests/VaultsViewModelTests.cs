using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="VaultsViewModel"/>: la lógica del formulario de administración de
/// vaults (listar, agregar una nueva carpeta raíz, cambiar de vault y quitar entradas).
/// Todos los efectos externos (selección de carpeta, apertura real del vault y
/// persistencia) se inyectan como delegados, así el VM queda libre de UI y de I/O.
/// </summary>
public class VaultsViewModelTests
{
    // ─── Test harness ─────────────────────────────────────────────────────────

    /// <summary>Construye el VM con delegados falsos y expone lo que registran.</summary>
    private sealed class Harness
    {
        public readonly List<string> Known;
        public readonly List<string> Opened  = new();
        public int                   Persisted;
        public string?               NextPick;
        public VaultsViewModel       Vm;

        public Harness(string? active = null, params string[] initial)
        {
            Known = new List<string>(initial);
            Vm = new VaultsViewModel(
                knownPaths: Known,
                activePath: active,
                pickFolder: () => NextPick,
                openVault:  p => Opened.Add(p),
                persist:    () => Persisted++);
        }
    }

    // ─── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Build_lists_every_known_vault()
    {
        var h = new Harness(null, @"C:\A", @"C:\B");

        Assert.Equal(2, h.Vm.Vaults.Count);
        Assert.Collection(h.Vm.Vaults,
            r => Assert.Equal(@"C:\A", r.FullPath),
            r => Assert.Equal(@"C:\B", r.FullPath));
    }

    [Fact]
    public void Row_name_is_the_folder_name_not_the_full_path()
    {
        var h = new Harness(null, @"C:\Vaults\Work");

        Assert.Equal("Work", h.Vm.Vaults[0].Name);
    }

    [Fact]
    public void Active_vault_is_flagged_case_insensitively()
    {
        var h = new Harness(@"c:\vaults\work", @"C:\Vaults\Work", @"C:\Vaults\Personal");

        Assert.True(h.Vm.Vaults[0].IsActive);
        Assert.False(h.Vm.Vaults[1].IsActive);
    }

    [Fact]
    public void HasVaults_is_false_when_none_are_known()
    {
        var h = new Harness();

        Assert.False(h.Vm.HasVaults);
    }

    // ─── Add (choose a new root folder) ───────────────────────────────────────

    [Fact]
    public void AddVault_registers_persists_and_opens_the_picked_folder()
    {
        var h = new Harness();
        h.NextPick = @"C:\Vaults\New";

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Contains(@"C:\Vaults\New", h.Known);
        Assert.Equal(1, h.Persisted);
        Assert.Equal(new[] { @"C:\Vaults\New" }, h.Opened);
        Assert.Equal(@"C:\Vaults\New", h.Vm.ActivePath);
    }

    [Fact]
    public void AddVault_marks_the_new_vault_active_in_the_list()
    {
        var h = new Harness();
        h.NextPick = @"C:\Vaults\New";

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Single(h.Vm.Vaults);
        Assert.True(h.Vm.Vaults[0].IsActive);
    }

    [Fact]
    public void AddVault_when_picker_is_cancelled_does_nothing()
    {
        var h = new Harness();
        h.NextPick = null; // user cancelled the folder dialog

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Empty(h.Known);
        Assert.Empty(h.Opened);
        Assert.Equal(0, h.Persisted);
    }

    [Fact]
    public void AddVault_does_not_duplicate_an_already_known_path()
    {
        var h = new Harness(null, @"C:\Vaults\Work");
        h.NextPick = @"c:\vaults\work"; // same folder, different casing

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Single(h.Known);
        Assert.Equal(0, h.Persisted);   // nothing new to persist
        Assert.Equal(new[] { @"c:\vaults\work" }, h.Opened); // but it still switches to it
    }

    // ─── Open (switch to an existing vault) ───────────────────────────────────

    [Fact]
    public void OpenVault_switches_the_active_vault()
    {
        var h = new Harness(@"C:\A", @"C:\A", @"C:\B");

        h.Vm.OpenVaultCommand.Execute(@"C:\B");

        Assert.Equal(new[] { @"C:\B" }, h.Opened);
        Assert.Equal(@"C:\B", h.Vm.ActivePath);
        Assert.False(h.Vm.Vaults[0].IsActive);
        Assert.True(h.Vm.Vaults[1].IsActive);
    }

    [Fact]
    public void OpenVault_ignores_a_null_or_empty_path()
    {
        var h = new Harness(@"C:\A", @"C:\A");

        h.Vm.OpenVaultCommand.Execute(null);

        Assert.Empty(h.Opened);
    }

    // ─── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveVault_drops_an_inactive_entry_and_persists()
    {
        var h = new Harness(@"C:\A", @"C:\A", @"C:\B");

        h.Vm.RemoveVaultCommand.Execute(@"C:\B");

        Assert.DoesNotContain(@"C:\B", h.Known);
        Assert.Equal(1, h.Persisted);
        Assert.Single(h.Vm.Vaults);
    }

    [Fact]
    public void RemoveVault_refuses_to_remove_the_active_vault()
    {
        var h = new Harness(@"C:\A", @"C:\A", @"C:\B");

        h.Vm.RemoveVaultCommand.Execute(@"C:\A");

        Assert.Contains(@"C:\A", h.Known);
        Assert.Equal(0, h.Persisted);
    }
}
