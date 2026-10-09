using System.Text.Json;

namespace HousecarlMcp;

/// <summary>The room a json write render's rows share once everything else is charged — #546's ceiling for the write
/// lanes, admitting each row on a cost measured before it is written (#859). Rows are the only part of these
/// documents that grows, so what the rest costs is the same document rendered with no rows at all: every list cut,
/// every count at its widest. Contract in docs/architecture/render-budget.md.</summary>
internal sealed class RowRoom
{
    readonly bool _unbounded, _floor;
    int _left;

    RowRoom(bool unbounded, bool floor, int left) { _unbounded = unbounded; _floor = floor; _left = left; }

    /// <summary>The whole document when it fits; else the rows that fit beside what it carries whatever the budget;
    /// when even that misses the cap, no rows, and the document's <c>max_chars_overrun</c> says so.</summary>
    public static string Fit(int cap, Func<RowRoom, string> render)
    {
        var whole = render(new RowRoom(unbounded: true, floor: false, left: 0));
        if (whole.Length <= cap) return whole;
        var floor = render(new RowRoom(unbounded: false, floor: true, left: 0));
        // The floor render states widest counts, so the answer that ships is always rendered with the true ones.
        return render(new RowRoom(unbounded: false, floor: false, left: Math.Max(cap - floor.Length, 0)));
    }

    /// <summary>Is the row written? Its cost is measured at the writer's depth; a list's first row also pays the
    /// close a non-empty list writes that an empty one does not. <paramref name="always"/> admits a row a render
    /// must show whatever it costs, and the floor render shows it too.</summary>
    public bool Admit(Utf8JsonWriter w, bool subsequent, Action<Utf8JsonWriter> row, bool always = false)
    {
        if (_unbounded) return true;
        int depth = w.CurrentDepth;
        int cost = JsonWire.MeasureUnit(depth, subsequent, row) + (subsequent ? 0 : JsonWire.NonEmptyCloseCost(depth));
        if (!always && cost > _left) return false;
        _left -= cost;
        return true;
    }

    /// <summary>The count a <c>rendered_*</c> member states: the real one, or in the floor render the total, which is
    /// at least as wide as any count the real render can write.</summary>
    public int Shown(int rendered, int total) => _floor ? total : rendered;
}
