using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// The Programmable I/O digital line table (record 0x37). The two fixtures are real: a factory-default
/// TM8110 readout (DBVer 0094, every line unassigned) and the TARPN TM8105 programming template
/// (DBVer 0095), a CPS save with AUX_GPI1 = Input / External PTT 1, IOP_GPIO2 = Output / Busy Status
/// and IOP_GPIO4 = Input / Unmute Audio Output Path with action parameters set.
/// </summary>
public class DigitalIoTests
{
    private const string DefaultTable =
        "960134E9FCC664A8000000000000C055004D3ABF3554000000000000002B80269D5F1A2A00000000000090194093CE6F0C860A0000000000006805D0A4F32B4305000000000000B60268D2F9CDA1020000000000005C0134E9FCC65001000000000080AE009A747E73A80000000000008067004D3ABF31182A000000000000F0194093CE6F2C860A0000000000008006D0A4F31B93A102000000000000A10134E9FCC666A80000000000008068004D3ABF311A2A000000000000301A4093CE6FAC860A0000000000009006C32FB21824A202000000000000";

    private const string TarpnTable =
        "960134E9FCC664A1204900000000C055004D3ABF3554000000000000002B80269D5F1A2A00000000000090194093CE6F0C860A0000000000006805D0A4F32B4305000000000000B60268D2F9CDA1020000000000005C0134E9FCC65001000000000080AE009A747E73A80000000000008067004D3ABF319828000000000000F0194093CE6F2C860A0000000000008006D0A4F31B938502820002000200A10134E9FCC666A80000000000008068004D3ABF311A2A000000000000301A4093CE6FAC860A0000000000009006C32FB21824A202000000000000";

    // Item index entries (7 bytes each) for the items the pdn-internal profile touches: the audio
    // block (0x3B, 95 bits x 4) and the digital line table (0x37, 132 bits x 15), as a real readout has them.
    private const string ItemIndex = "3B5F0004000900" + "3784000F000600";

    private static CodeplugFields Open(string tableHex)
    {
        byte[] table = Convert.FromHexString(tableHex);
        var image = new CodeplugImage(
            [new KeyValuePair<string, string>("DBVer", "0095")],
            [
                new CodeplugRecord(0x01, 0, Convert.FromHexString(ItemIndex)),
                new CodeplugRecord(0x09, 0, new byte[37]),
                new CodeplugRecord(0x3B, 0, new byte[20]),
            ]);
        image.SetSectionBytes(0x37, table);
        return CodeplugFields.Open(image);
    }

    [Fact]
    public void Pdn_internal_profile_routes_everything_to_the_internal_options_board()
    {
        CodeplugFields f = Open(DefaultTable);

        f.ApplyPdnInternal();

        // includes pdn-extra (and so pdn-basic)
        f.CcdiModeAllowed.Should().BeTrue();
        f.PowerupState.Should().Be(DataPowerupMode.CommandMode);
        f.CommandModeBaud.Should().Be(FfskBaud.Baud28800);
        f.TransparentModeEnabled.Should().BeTrue();
        // the internal-options additions
        f.DataPort.Should().Be(DataPort.InternalOptions);
        f.CommandModeFlowControl.Should().Be(DataFlowControl.None);
        f.GetRxTapOutNode().Should().Be(2);
        f.TapOutUnmute.Should().Be(TapOutUnmute.BusyDetectSubaudible);
        f.GetEptt1TapInNode().Should().Be(13);
        // the audio block a bench radio with the board fitted carries, byte for byte
        Convert.ToHexString(f.Image.Require(0x3B, 0).Data).Should().Be("000100C2048000004000803A0020004000001000");
        f.GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        // and nothing else on the line table moved
        foreach (DigitalIoLine other in Enum.GetValues<DigitalIoLine>().Where(l => l != DigitalIoLine.IopGpio1))
        {
            f.GetDigitalIoRole(other).Should().Be(DigitalIoRole.Unassigned, other.ToString());
        }
    }

    [Fact]
    public void The_table_is_chunked_into_records_the_way_the_cps_chunks_it()
    {
        CodeplugFields f = Open(DefaultTable);
        f.Image.Records.Where(r => r.Section == 0x37).Select(r => r.Data.Length).Should().Equal(32, 32, 32, 32, 32, 32, 24);
    }

    [Fact]
    public void Labels_are_the_cps_pin_column()
    {
        CodeplugFields f = Open(DefaultTable);
        f.GetDigitalIoLabel(DigitalIoLine.AuxGpi1).Should().Be("PIN_12");
        f.GetDigitalIoLabel(DigitalIoLine.AuxGpio7).Should().Be("PIN_1");
        f.GetDigitalIoLabel(DigitalIoLine.IopGpio1).Should().Be("PIN_9");
        f.GetDigitalIoLabel(DigitalIoLine.IopGpio7).Should().Be("PIN_15");
        f.GetDigitalIoLabel(DigitalIoLine.ChGpio1).Should().Be("C_HEAD");
    }

    [Fact]
    public void A_default_codeplug_has_every_line_unassigned()
    {
        CodeplugFields f = Open(DefaultTable);
        foreach (DigitalIoLine line in Enum.GetValues<DigitalIoLine>())
        {
            f.GetDigitalIoRole(line).Should().Be(DigitalIoRole.Unassigned, line.ToString());
        }
    }

    [Fact]
    public void The_tarpn_template_reads_back_as_the_cps_shows_it()
    {
        CodeplugFields f = Open(TarpnTable);
        f.GetDigitalIoRole(DigitalIoLine.AuxGpi1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        f.GetDigitalIoRole(DigitalIoLine.IopGpio2).Should().Be(DigitalIoRole.BusyStatusOutput);
        f.GetDigitalIoRole(DigitalIoLine.IopGpio4).Should().Be(DigitalIoRole.Other); // Unmute Audio Output Path, unmapped
        f.GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.Unassigned);
        f.GetDigitalIoRole(DigitalIoLine.ChGpio1).Should().Be(DigitalIoRole.Unassigned);
    }

    [Fact]
    public void Setting_a_line_changes_only_that_lines_configuration_bits()
    {
        CodeplugFields f = Open(DefaultTable);
        byte[] before = f.Image.SectionBytes(0x37);

        f.SetDigitalIoRole(DigitalIoLine.IopGpio1, DigitalIoRole.ExternalPtt1Input);

        byte[] after = f.Image.SectionBytes(0x37);
        after.Should().HaveCount(before.Length);
        f.GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.ExternalPtt1Input);
        f.GetDigitalIoLabel(DigitalIoLine.IopGpio1).Should().Be("PIN_9");
        foreach (DigitalIoLine other in Enum.GetValues<DigitalIoLine>().Where(l => l != DigitalIoLine.IopGpio1))
        {
            f.GetDigitalIoRole(other).Should().Be(DigitalIoRole.Unassigned, other.ToString());
        }

        // IOP_GPIO1 is entry 7; entries 0-6 are 118+111+111+118+111+111+111 = 791 bits, its header
        // and 5-char label another 49, so its configuration starts at bit 840 = byte 105 exactly.
        after[..105].Should().Equal(before[..105]);
        after[113..].Should().Equal(before[113..]);
    }

    [Fact]
    public void Setting_external_ptt_reproduces_the_cps_bytes_for_that_configuration()
    {
        // The TARPN template's AUX_GPI1 is the CPS's own encoding of Input / External PTT 1. Writing
        // that role onto AUX_GPI1 of a default table must produce those bytes, and clearing it must
        // give the default table back.
        CodeplugFields f = Open(DefaultTable);
        f.SetDigitalIoRole(DigitalIoLine.AuxGpi1, DigitalIoRole.ExternalPtt1Input);
        f.SetDigitalIoRole(DigitalIoLine.IopGpio2, DigitalIoRole.BusyStatusOutput);

        byte[] tarpn = Convert.FromHexString(TarpnTable);
        byte[] ours = f.Image.SectionBytes(0x37);
        // Everything up to IOP_GPIO4 (entry 10, the unmapped Unmute line) must match the template.
        // Entry 10's configuration starts at bit 1181 = byte 147.
        ours[..147].Should().Equal(tarpn[..147]);

        f.SetDigitalIoRole(DigitalIoLine.AuxGpi1, DigitalIoRole.Unassigned);
        f.SetDigitalIoRole(DigitalIoLine.IopGpio2, DigitalIoRole.Unassigned);
        Convert.ToHexString(f.Image.SectionBytes(0x37)).Should().Be(DefaultTable);
    }

    [Fact]
    public void An_unrecognised_configuration_is_preserved_and_cannot_be_written()
    {
        CodeplugFields f = Open(TarpnTable);
        Action act = () => f.SetDigitalIoRole(DigitalIoLine.IopGpio1, DigitalIoRole.Other);
        act.Should().Throw<ArgumentException>();
        Convert.ToHexString(f.Image.SectionBytes(0x37)).Should().Be(TarpnTable);
    }

    [Fact]
    public void An_input_only_line_refuses_to_be_an_output()
    {
        CodeplugFields f = Open(DefaultTable);
        Action act = () => f.SetDigitalIoRole(DigitalIoLine.AuxGpi2, DigitalIoRole.BusyStatusOutput);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_table_of_another_shape_is_refused_rather_than_misread()
    {
        CodeplugFields f = Open("00FF" + DefaultTable[4..]);
        Action act = () => f.GetDigitalIoRole(DigitalIoLine.AuxGpi1);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_resized_line_round_trips_through_m8p()
    {
        CodeplugFields f = Open(DefaultTable);
        f.SetDigitalIoRole(DigitalIoLine.IopGpio1, DigitalIoRole.ExternalPtt1Input);
        CodeplugImage reloaded = CodeplugImage.LoadM8p(f.Image.ToM8p());
        CodeplugFields.Open(reloaded).GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.ExternalPtt1Input);
    }

    [Fact]
    public void The_console_exposes_lines_by_their_manual_names()
    {
        CodeplugFields f = Open(TarpnTable);
        FieldConsole.Get(f, "gpio.aux_gpi1").Should().Be("ExternalPtt1Input");
        FieldConsole.Get(f, "gpio.iop_gpio1").Should().Be("Unassigned");
        FieldConsole.Set(f, "gpio.iop_gpio1", "ExternalPtt1Input");
        FieldConsole.Get(f, "gpio.iop_gpio1").Should().Be("ExternalPtt1Input");
        FieldConsole.Set(f, "gpio.IOP_GPIO1", "unassigned");
        f.GetDigitalIoRole(DigitalIoLine.IopGpio1).Should().Be(DigitalIoRole.Unassigned);
    }
}
