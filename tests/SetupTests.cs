using Xunit;

namespace Ecoar.Tests;

public class SetupTests
{
    private const string Ours = "{5E0C2A91-3B7D-4C6E-9F18-A2D4B6C8E0F1}";
    private const string Foreign = "{11111111-2222-3333-4444-555555555555}";
    private const string Default = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
    private const string Raw = "{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}";
    private const string Comms = "{98951333-B9CD-48B1-A0A3-FF40682D73F7}";

    private static Dictionary<string, object?> Fx(object? sfx = null, object? sfxModes = null, object? mfx = null, object? mfxModes = null)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (sfx is not null) d[Setup.SfxValueName] = sfx;
        if (sfxModes is not null) d[Setup.ModesValueName] = sfxModes;
        if (mfx is not null) d[Setup.MfxValueName] = mfx;
        if (mfxModes is not null) d[Setup.MfxModesValueName] = mfxModes;
        return d;
    }

    [Fact]
    public void Stream_slot_takes_default_and_mode_slot_takes_raw()
    {
        // Discord captures in RAW mode, which skips SFXs; an MFX that declares RAW still runs.
        Assert.Equal(new[] { Default }, Setup.OurModes);
        Assert.Equal(new[] { Raw }, Setup.OurMfxModes);
    }

    [Fact]
    public void PlanInstall_empty_writes_both_slots()
    {
        Assert.Equal(new[] { SetupAction.WriteSfx, SetupAction.WriteModes, SetupAction.WriteMfx, SetupAction.WriteMfxModes },
            Setup.PlanInstall(Fx()));
    }

    [Fact]
    public void PlanInstall_foreign_sfx_refuses()
    {
        Assert.Equal(new[] { SetupAction.RefuseForeignSfx }, Setup.PlanInstall(Fx(Foreign, new[] { Default })));
    }

    [Fact]
    public void PlanInstall_foreign_mfx_refuses()
    {
        Assert.Equal(new[] { SetupAction.RefuseForeignMfx }, Setup.PlanInstall(Fx(mfx: Foreign, mfxModes: new[] { Raw })));
    }

    [Fact]
    public void PlanInstall_already_ours_is_idempotent()
    {
        Assert.Equal(new[] { SetupAction.SkipModesAlreadyOurs, SetupAction.SkipModesAlreadyOurs },
            Setup.PlanInstall(Fx(Ours.ToLowerInvariant(), new[] { Default }, Ours, new[] { Raw })));
    }

    [Fact]
    public void PlanInstall_upgrades_previous_sfx_only_install()
    {
        Assert.Equal(new[] { SetupAction.WriteModes, SetupAction.WriteMfx, SetupAction.WriteMfxModes },
            Setup.PlanInstall(Fx(Ours, new[] { Default, Raw })));
    }

    [Fact]
    public void PlanInstall_keeps_foreign_modes_list()
    {
        Assert.Equal(new[] { SetupAction.WriteSfx, SetupAction.SkipModesAlreadyOurs, SetupAction.WriteMfx, SetupAction.WriteMfxModes },
            Setup.PlanInstall(Fx(null, new[] { Default, Comms })));
    }

    [Fact]
    public void PlanUninstall_removes_only_ours()
    {
        Assert.Equal(new[] { SetupAction.RemoveSfx, SetupAction.RemoveModes, SetupAction.RemoveMfx, SetupAction.RemoveMfxModes },
            Setup.PlanUninstall(Fx(Ours, new[] { Default }, Ours, new[] { Raw })));
        Assert.Equal(new[] { SetupAction.RemoveSfx, SetupAction.RemoveModes },
            Setup.PlanUninstall(Fx(Ours, new[] { Default, Raw })));   // previous install
        Assert.Equal(new[] { SetupAction.RemoveSfx },
            Setup.PlanUninstall(Fx(Ours, new[] { Default, Comms }, Foreign, new[] { Raw })));
        Assert.Empty(Setup.PlanUninstall(Fx(Foreign, new[] { Default })));
    }
}
