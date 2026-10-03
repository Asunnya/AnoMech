using System.Globalization;
using AnoMech.Core.Native.Interfaces;
using Microsoft.VisualBasic.FileIO;

namespace AnoMech.Tests;

// Real sheet rows from the ffxiv-datamining CSVs that AnoMech.Tests.csproj downloads at build.
// Mirrors GameData's mapping column for column; sheets are parsed once per test run.
internal sealed class DataminingGameData(FakeRsvFunctions rsv) : IGameData
{
    private static readonly Lazy<Sheet> Actions = Load("Action");
    private static readonly Lazy<Sheet> BNpcBases = Load("BNpcBase");
    private static readonly Lazy<Sheet> BNpcNames = Load("BNpcName");
    private static readonly Lazy<Sheet> ClassJobs = Load("ClassJob");
    private static readonly Lazy<Sheet> Knockbacks = Load("Knockback");
    private static readonly Lazy<Sheet> ModelCharas = Load("ModelChara");
    private static readonly Lazy<Sheet> ModelSkeletons = Load("ModelSkeleton");
    private static readonly Lazy<Sheet> Omens = Load("Omen");
    private static readonly Lazy<Sheet> Statuses = Load("Status");

    public ActionRow? Action(uint actionId)
    {
        if (Actions.Value.Row(actionId) is not { } row) return null;
        return new ActionRow(
            actionId,
            rsv.Resolve(row.Text("Name")),
            row.UInt("Cast100ms") / 10f,
            row.Byte("CastType"),
            row.Byte("EffectRange"),
            row.Byte("XAxisModifier"),
            OmenPath(row.UInt("Omen")),
            OmenPath(row.UInt("OmenAlt")),
            row.UInt("ActionCategory"),
            row.Bool("CanTargetSelf"),
            row.Bool("CanTargetParty"));
    }

    public KnockbackRow? Knockback(uint knockbackId)
        => Knockbacks.Value.Row(knockbackId) is { } row
            ? new KnockbackRow(knockbackId, row.Byte("Distance"), row.Byte("Speed"))
            : null;

    public ClassJobRow? ClassJob(uint classJobId)
        => ClassJobs.Value.Row(classJobId) is { } row
            ? new ClassJobRow(classJobId, row.UInt("LimitBreak1"), row.UInt("LimitBreak2"), row.UInt("LimitBreak3"))
            : null;

    public BNpcBaseRow? BNpcBase(uint bnpcBaseId)
        => BNpcBases.Value.Row(bnpcBaseId) is { } row
            ? new BNpcBaseRow(bnpcBaseId, row.Float("Scale"), row.UInt("ModelChara"))
            : null;

    public ModelCharaRow? ModelChara(uint modelCharaId)
        => ModelCharas.Value.Row(modelCharaId) is { } row
            ? new ModelCharaRow(modelCharaId, row.Byte("Type"), row.UShort("Model"), row.Float("Radius"))
            : null;

    public ModelSkeletonRow? ModelSkeleton(uint skeletonId)
        => ModelSkeletons.Value.Row(skeletonId) is { } row
            ? new ModelSkeletonRow(skeletonId, row.Float("Radius"))
            : null;

    public string? StatusName(ushort statusId) => NonEmpty(Resolve(Statuses.Value.Row(statusId)?.Text("Name")));

    public string? BNpcName(uint nameId) => NonEmpty(Resolve(BNpcNames.Value.Row(nameId)?.Text("Singular")));

    public bool FileExists(string path) => true;

    private static string? OmenPath(uint omenId)
        => omenId != 0 && Omens.Value.Row(omenId) is { } omen ? omen.Text("Path") : null;

    private string? Resolve(string? text) => text == null ? null : rsv.Resolve(text);

    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static Lazy<Sheet> Load(string name) => new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "datamining", name + ".csv");
        if (!File.Exists(path))
            throw new FileNotFoundException($"{name}.csv was not downloaded; add {name} to the DataminingSheet items in AnoMech.Tests.csproj.", path);

        using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields()!;
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++)
            columns.TryAdd(header[i], i);

        var rows = new Dictionary<uint, string[]>();
        while (parser.ReadFields() is { } fields)
            rows[uint.Parse(fields[0], CultureInfo.InvariantCulture)] = fields;
        return new Sheet(name, columns, rows);
    });

    private sealed class Sheet(string name, Dictionary<string, int> columns, Dictionary<uint, string[]> rows)
    {
        public SheetRow? Row(uint id) => rows.TryGetValue(id, out var fields) ? new SheetRow(this, fields) : null;

        public int Column(string column)
            => columns.TryGetValue(column, out var index) ? index : throw new KeyNotFoundException($"{name}.csv has no column '{column}'.");
    }

    private readonly struct SheetRow(Sheet sheet, string[] fields)
    {
        public string Text(string column) => fields[sheet.Column(column)];
        public uint UInt(string column) => uint.Parse(Text(column), CultureInfo.InvariantCulture);
        public byte Byte(string column) => byte.Parse(Text(column), CultureInfo.InvariantCulture);
        public ushort UShort(string column) => ushort.Parse(Text(column), CultureInfo.InvariantCulture);
        public float Float(string column) => float.Parse(Text(column), CultureInfo.InvariantCulture);
        public bool Bool(string column) => bool.Parse(Text(column));
    }
}
