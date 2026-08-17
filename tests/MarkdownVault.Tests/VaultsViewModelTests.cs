using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="VaultsViewModel"/>: la lógica del formulario de administración de
/// vaults (listar, agregar una nueva carpeta raíz, abrir/cerrar como conjunto de vaults
/// abiertos — Model A multi-root, sin selección única — y quitar entradas). Todos los
/// efectos externos (selección de carpeta, apertura/cierre real de un root y persistencia)
/// se inyectan como delegados, así el VM queda libre de UI y de I/O.
///
/// Nota: este archivo fue actualizado mecánicamente a la API open-set del Phase 4
/// (Activate/IsActive → ToggleOpen/IsOpen) para mantener la solución compilando tras el
/// cambio de contrato de VaultsViewModel. Task 8.4 (tests-last) es dueña de la cobertura
/// completa del comportamiento open-set (varios vaults abiertos a la vez, migración, etc.).
/// </summary>
public class VaultsViewModelTests
{
    // ─── Test harness ─────────────────────────────────────────────────────────

    /// <summary>Construye el VM con delegados falsos y expone lo que registran.</summary>
    private sealed class Harness
    {
        public readonly List<string> Known;

        // Mimics FileService.VaultRoots: mutated in place by the injected openRoot/closeRoot
        // delegates below, so IsOpen recomputes live instead of a ctor-time snapshot.
        public readonly List<string> OpenRoots;
        public int                   Persisted;
        public string?               NextPick;
        public VaultsViewModel       Vm;

        public Harness(string[]? open = null, params string[] initial)
        {
            Known     = new List<string>(initial);
            OpenRoots = new List<string>(open ?? Array.Empty<string>());
            Vm = new VaultsViewModel(
                knownPaths: Known,
                openPaths:  () => OpenRoots,
                pickFolder: () => NextPick,
                openRoot:   p => { if (!OpenRoots.Contains(p)) OpenRoots.Add(p); },
                closeRoot:  p => OpenRoots.Remove(p),
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
    public void Open_vault_is_flagged_case_insensitively()
    {
        var h = new Harness(new[] { @"c:\vaults\work" }, @"C:\Vaults\Work", @"C:\Vaults\Personal");

        Assert.True(h.Vm.Vaults[0].IsOpen);
        Assert.False(h.Vm.Vaults[1].IsOpen);
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
        Assert.Equal(new[] { @"C:\Vaults\New" }, h.OpenRoots);
    }

    [Fact]
    public void AddVault_marks_the_new_vault_open_in_the_list()
    {
        var h = new Harness();
        h.NextPick = @"C:\Vaults\New";

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Single(h.Vm.Vaults);
        Assert.True(h.Vm.Vaults[0].IsOpen);
    }

    [Fact]
    public void AddVault_when_picker_is_cancelled_does_nothing()
    {
        var h = new Harness();
        h.NextPick = null; // user cancelled the folder dialog

        h.Vm.AddVaultCommand.Execute(null);

        Assert.Empty(h.Known);
        Assert.Empty(h.OpenRoots);
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
        Assert.Equal(new[] { @"c:\vaults\work" }, h.OpenRoots); // but it still opens it
    }

    // ─── ToggleOpen (open-set: several vaults open at once) ────────────────────

    [Fact]
    public void ToggleOpen_opens_a_closed_vault_without_closing_others()
    {
        var h = new Harness(new[] { @"C:\A" }, @"C:\A", @"C:\B");

        h.Vm.ToggleOpenCommand.Execute(@"C:\B");

        Assert.Contains(@"C:\B", h.OpenRoots);
        Assert.Contains(@"C:\A", h.OpenRoots); // still open — no single-active swap
        Assert.True(h.Vm.Vaults[0].IsOpen);
        Assert.True(h.Vm.Vaults[1].IsOpen);
    }

    [Fact]
    public void ToggleOpen_closes_an_open_vault()
    {
        var h = new Harness(new[] { @"C:\A", @"C:\B" }, @"C:\A", @"C:\B");

        h.Vm.ToggleOpenCommand.Execute(@"C:\B");

        Assert.DoesNotContain(@"C:\B", h.OpenRoots);
        Assert.Contains(@"C:\A", h.OpenRoots);
        Assert.True(h.Vm.Vaults[0].IsOpen);
        Assert.False(h.Vm.Vaults[1].IsOpen);
    }

    [Fact]
    public void ToggleOpen_ignores_a_null_or_empty_path()
    {
        var h = new Harness(new[] { @"C:\A" }, @"C:\A");

        h.Vm.ToggleOpenCommand.Execute(null);

        Assert.Equal(new[] { @"C:\A" }, h.OpenRoots);
    }

    [Fact]
    public void ToggleOpen_twice_on_the_same_vault_opens_then_closes_it()
    {
        var h = new Harness(null, @"C:\A", @"C:\B");

        h.Vm.ToggleOpenCommand.Execute(@"C:\A");
        Assert.True(h.Vm.Vaults[0].IsOpen);
        Assert.Contains(@"C:\A", h.OpenRoots);

        h.Vm.ToggleOpenCommand.Execute(@"C:\A");
        Assert.False(h.Vm.Vaults[0].IsOpen);
        Assert.DoesNotContain(@"C:\A", h.OpenRoots);
    }

    [Fact]
    public void ToggleOpen_supports_several_vaults_open_simultaneously()
    {
        var h = new Harness(null, @"C:\A", @"C:\B", @"C:\C");

        h.Vm.ToggleOpenCommand.Execute(@"C:\A");
        h.Vm.ToggleOpenCommand.Execute(@"C:\B");
        h.Vm.ToggleOpenCommand.Execute(@"C:\C");

        Assert.Equal(new[] { @"C:\A", @"C:\B", @"C:\C" }, h.OpenRoots);
        Assert.All(h.Vm.Vaults, v => Assert.True(v.IsOpen));

        // Closing one leaves the others open — no single-active swap anywhere in the flow.
        h.Vm.ToggleOpenCommand.Execute(@"C:\B");
        Assert.Equal(new[] { @"C:\A", @"C:\C" }, h.OpenRoots);
        Assert.True(h.Vm.Vaults[0].IsOpen);
        Assert.False(h.Vm.Vaults[1].IsOpen);
        Assert.True(h.Vm.Vaults[2].IsOpen);
    }

    // ─── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveVault_drops_a_closed_entry_and_persists()
    {
        var h = new Harness(new[] { @"C:\A" }, @"C:\A", @"C:\B");

        h.Vm.RemoveVaultCommand.Execute(@"C:\B");

        Assert.DoesNotContain(@"C:\B", h.Known);
        Assert.Equal(1, h.Persisted);
        Assert.Single(h.Vm.Vaults);
    }

    [Fact]
    public void RemoveVault_refuses_to_remove_an_open_vault()
    {
        var h = new Harness(new[] { @"C:\A" }, @"C:\A", @"C:\B");

        h.Vm.RemoveVaultCommand.Execute(@"C:\A");

        Assert.Contains(@"C:\A", h.Known);
        Assert.Equal(0, h.Persisted);
    }
}
