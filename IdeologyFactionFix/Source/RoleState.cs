using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client;
using RimWorld;
using Verse;

namespace IdeologyFactionFix;

public sealed class FactionRoleState : IExposable
{
    public Precept_RoleSingle role;
    public Faction faction;
    public IdeoRoleInstance instance;
    public bool active;
    private Pawn snapshotPawn;
    private Pawn[] snapshot = Array.Empty<Pawn>();
    public IEnumerable<Pawn> ChosenPawns()
    {
        var pawn = instance?.pawn;
        if (pawn == null) return Array.Empty<Pawn>();
        if (snapshotPawn != pawn) { snapshotPawn = pawn; snapshot = new[] { pawn }; }
        return snapshot;
    }
    public FactionRoleState() { }
    public FactionRoleState(Precept_RoleSingle role, Faction faction)
    {
        this.role = role; this.faction = faction;
        var legacy = role.chosenPawn;
        instance = new IdeoRoleInstance(role);
        if (legacy?.pawn?.Faction == faction) { instance.pawn = legacy.pawn; instance.abilities = legacy.abilities; }
        int count = ColonyScope.Count(role.ideo, faction);
        active = role.def.leaderRole || (role.def.activationBelieverCount >= 0 && count >= role.def.activationBelieverCount) || (instance.pawn != null && role.active && count > role.def.deactivationBelieverCount);
    }
    public void ExposeData()
    {
        Scribe_References.Look(ref role, "role");
        Scribe_References.Look(ref faction, "faction");
        Scribe_Values.Look(ref active, "active");
        if ((Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit) && instance != null) instance.sourceRole = role;
        Scribe_Deep.Look(ref instance, "instance", role);
    }
}

public sealed class IdeologyColonyData : GameComponent
{
    public List<FactionRoleState> roles = new();
    private Dictionary<(Precept_RoleSingle, Faction), FactionRoleState> index = new();
    public IdeologyColonyData(Game game) { }
    public static IdeologyColonyData Current => Verse.Current.Game?.GetComponent<IdeologyColonyData>();
    public FactionRoleState Read(Precept_RoleSingle role, Faction faction) => index.TryGetValue((role, faction), out var state) ? state : new FactionRoleState(role, faction);
    public void Store(FactionRoleState state)
    {
        var key = (state.role, state.faction);
        if (!index.ContainsKey(key)) roles.Add(state);
        index[key] = state;
    }
    public bool Has(Precept_RoleSingle role) => roles.Any(s => s.role == role);
    public bool Has(Precept_RoleSingle role, Faction faction) => index.ContainsKey((role,faction));
    public void DiscardRemovedRoles()
    {
        for (int i = roles.Count-1; i >= 0; i--)
            if (roles[i].role?.ideo?.PreceptsListForReading.Contains(roles[i].role) != true)
            { index.Remove((roles[i].role,roles[i].faction)); roles.RemoveAt(i); }
    }
    public IEnumerable<Pawn> AllHolders(Precept_RoleSingle role)
    {
        for (int i = 0; i < roles.Count; i++)
            if (roles[i].role == role && roles[i].instance?.pawn != null) yield return roles[i].instance.pawn;
    }
    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving) roles.Sort((a,b) => a.role == b.role ? a.faction.loadID.CompareTo(b.faction.loadID) : string.CompareOrdinal(a.role.GetUniqueLoadID(), b.role.GetUniqueLoadID()));
        Scribe_Collections.Look(ref roles, "ideologyFactionRoles", LookMode.Deep);
        roles ??= new();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            index.Clear();
            roles.RemoveAll(s => s.role == null || s.faction == null || s.instance == null);
            foreach (var state in roles) { state.instance.sourceRole = state.role; index[(state.role, state.faction)] = state; }
        }
    }
}

// Fields are borrowed only for the duration of a role operation. The original
// precept identity remains intact for synced commands, abilities and ritual refs.
public sealed class RoleScope : IDisposable
{
    [ThreadStatic] private static RoleScope current;
    [ThreadStatic] private static Stack<RoleScope> pool;
    private RoleScope previous;
    private Precept_RoleSingle role;
    private Faction faction;
    private IdeoRoleInstance saved;
    private bool savedActive;
    private FactionRoleState state;
    private IdeologyColonyData data;
    private bool commit;
    private bool disposed;
    private void Initialize(Precept_RoleSingle role, Faction faction, bool commit)
    {
        disposed = false;
        previous = current; this.role = role; this.faction = faction; this.commit = commit;
        data = IdeologyColonyData.Current;
        saved = role.chosenPawn; savedActive = role.active;
        // A UI read inside another faction's temporary scope must not migrate
        // that temporary holder as if it were legacy save data.
        RoleScope ancestor = null;
        for (var scope = previous; scope != null; scope = scope.previous) if (scope.role == role) ancestor = scope;
        if (ancestor != null)
        {
            var borrowed = role.chosenPawn; bool borrowedActive = role.active;
            role.chosenPawn = ancestor.saved; role.active = ancestor.savedActive;
            try { state = data.Read(role, faction); }
            finally { role.chosenPawn = borrowed; role.active = borrowedActive; }
        }
        else state = data.Read(role, faction);
        FactionContext.Push(faction);
        role.chosenPawn = state.instance; role.active = state.active; current = this;
    }
    public static bool IsCurrent(Precept_RoleSingle role) => current?.role == role;
    public static RoleScope Enter(Precept_RoleSingle role, Faction faction, bool commit)
    {
        if (!ColonyScope.Enabled || !ColonyScope.IsColony(faction) || IdeologyColonyData.Current == null) return null;
        if (current?.role == role && current.faction == faction) { current.commit |= commit; return null; }
        pool ??= new Stack<RoleScope>(32);
        var scope = pool.Count > 0 ? pool.Pop() : new RoleScope();
        scope.Initialize(role, faction, commit);
        return scope;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (commit)
            {
                state.instance = role.chosenPawn; state.active = role.active;
                data.Store(state);
            }
        }
        finally
        {
            role.chosenPawn = saved; role.active = savedActive; current = previous; FactionContext.Pop();
            previous = null; role = null; faction = null; saved = null; state = null; data = null;
            if (pool.Count < 32) pool.Push(this);
        }
    }
}
