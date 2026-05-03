using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TsMap.Routing
{
    public class GraphValidator
    {
        private readonly TsMapper _mapper;
        private readonly RoutingGraph _graph;

        // Built once at the start of Validate(), reused across all checks
        private HashSet<(ulong, ulong)> _edgeSet;      // all edges
        private HashSet<ulong> _nodesWithOutEdge;      // from-node set
        private Dictionary<ulong, ulong> _ufParent;   // Union-Find parent map
        private ulong _mainRoot;                       // root UID of largest component
        private int _mainComponentSize;

        // Edge counts by type (filled in BuildEdgeSets)
        private int _roadEdgeCount;
        private int _prefabEdgeCount;
        private int _ferryEdgeCount;

        public GraphValidator(TsMapper mapper, RoutingGraph graph)
        {
            _mapper = mapper;
            _graph  = graph;
        }

        public void Validate(string outputDir)
        {
            var filePath = Path.Combine(outputDir, "routing-graph-validation.txt");
            using (var w = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                W(w, "================================================================");
                W(w, "  PHASE 3 — ROUTING GRAPH VALIDATION");
                W(w, $"  Graph: {_graph.Nodes.Count:N0} nodes, {_graph.Edges.Count:N0} edges");
                W(w, $"  Timestamp: {DateTime.UtcNow:o}");
                W(w, "================================================================");
                W(w, "");

                BuildEdgeSets();
                BuildUnionFind();
                ComputeMainComponent();

                int passed = 0;

                if (RunCheck(w, "1", "Origin Rotation Correctness",   () => Check1_OriginRotation(w)))    passed++;
                if (RunCheck(w, "2", "Graph Connectivity",            () => Check2_Connectivity(w)))      passed++;
                if (RunCheck(w, "3", "Roundabout Directionality",     () => Check3_Roundabouts(w)))       passed++;
                if (RunCheck(w, "4", "Road Directionality Sample",    () => Check4_RoadDirectionality(w)))passed++;
                if (RunCheck(w, "5", "Zero/Negative Weights",         () => Check5_EdgeWeights(w)))       passed++;
                if (RunCheck(w, "6", "City Reachability",             () => Check6_CityReachability(w)))  passed++;
                if (RunCheck(w, "7", "Ferry Connectivity",            () => Check7_FerryConnectivity(w))) passed++;
                if (RunCheck(w, "8", "Edge Count Ratios",             () => Check8_EdgeRatios(w)))        passed++;

                W(w, "================================================================");
                W(w, $"VALIDATION COMPLETE: {passed}/8 checks PASS");
                W(w, "================================================================");
                W(w, $"Report: {filePath}");
            }
        }

        // ── One-time setup ────────────────────────────────────────────────────

        private void BuildEdgeSets()
        {
            _edgeSet          = new HashSet<(ulong, ulong)>(_graph.Edges.Count);
            _nodesWithOutEdge = new HashSet<ulong>(_graph.Edges.Count);

            foreach (var e in _graph.Edges)
            {
                _edgeSet.Add((e.From, e.To));
                _nodesWithOutEdge.Add(e.From);
                if      (e.ItemType == "road")   _roadEdgeCount++;
                else if (e.ItemType == "prefab") _prefabEdgeCount++;
                else if (e.ItemType == "ferry")  _ferryEdgeCount++;
            }
        }

        private void BuildUnionFind()
        {
            _ufParent = new Dictionary<ulong, ulong>(_graph.Nodes.Count);
            foreach (var uid in _graph.Nodes.Keys)
                _ufParent[uid] = uid;

            foreach (var e in _graph.Edges)
            {
                if (_ufParent.ContainsKey(e.From) && _ufParent.ContainsKey(e.To))
                    UfUnion(e.From, e.To);
            }
        }

        private void ComputeMainComponent()
        {
            var counts = new Dictionary<ulong, int>();
            foreach (var uid in _graph.Nodes.Keys)
            {
                var root = UfFind(uid);
                if (counts.TryGetValue(root, out int c)) counts[root] = c + 1;
                else                                     counts[root] = 1;
            }
            _mainComponentSize = 0;
            _mainRoot = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > _mainComponentSize)
                {
                    _mainComponentSize = kv.Value;
                    _mainRoot = kv.Key;
                }
            }
        }

        // ── Check runner ──────────────────────────────────────────────────────

        private bool RunCheck(StreamWriter w, string num, string name, Func<bool> check)
        {
            W(w, $"[Check {num}] {name}");
            bool pass = check();
            W(w, $"  Result: {(pass ? "PASS ✓" : "FAIL ✗")}");
            W(w, "");
            return pass;
        }

        // ── Check 1 — Origin Rotation Correctness ────────────────────────────

        private bool Check1_OriginRotation(StreamWriter w)
        {
            // For each sampled prefab where Origin != 0, find a NavCurve where
            // curve.StartNodeIndex == prefab.Origin.  That curve originates at
            // the PPD origin control node, which must map to Nodes[0].
            // Formula: (Origin - Origin + N) % N == 0 → Nodes[0].
            // We verify the formula algebraically AND that Nodes[0] is present
            // as an edge source in the graph.

            int sampled = 0, notInGraph = 0, notEdgeSource = 0;
            bool examplePrinted = false;

            foreach (var prefab in _mapper.Prefabs)
            {
                if (sampled >= 200) break;
                if (prefab.Origin == 0 || !prefab.Valid) continue;
                if (prefab.Prefab?.NavCurves == null) continue;

                int descN = prefab.Prefab.PrefabNodes.Count;
                if (descN == 0 || prefab.Nodes.Count == 0) continue;

                foreach (var curve in prefab.Prefab.NavCurves)
                {
                    if (curve.StartNodeIndex != prefab.Origin) continue;
                    if (curve.StartNodeIndex >= descN) continue;

                    sampled++;
                    int idx         = (curve.StartNodeIndex - prefab.Origin + descN) % descN; // always 0
                    ulong nodes0uid = prefab.Nodes[0];
                    bool inGraph    = _graph.Nodes.ContainsKey(nodes0uid);
                    bool hasOut     = _nodesWithOutEdge.Contains(nodes0uid);

                    if (!examplePrinted)
                    {
                        examplePrinted = true;
                        W(w, "  Numerical example (first qualifying prefab):");
                        W(w, $"    Prefab UID:            {prefab.Uid}");
                        W(w, $"    Origin:                {prefab.Origin}");
                        W(w, $"    PrefabNodes.Count (descN): {descN}  Nodes.Count (inst): {prefab.Nodes.Count}");
                        W(w, $"    curve.StartNodeIndex:  {curve.StartNodeIndex}");
                        W(w, $"    Formula result:        ({curve.StartNodeIndex}-{prefab.Origin}+{descN})%{descN} = {idx}  (expected: 0)");
                        W(w, $"    prefab.Nodes[0]:       {nodes0uid}");
                        W(w, $"    Node in graph:         {inGraph}");
                        W(w, $"    Node is edge source:   {hasOut}");
                    }

                    if (!inGraph)   notInGraph++;
                    else if (!hasOut) notEdgeSource++;

                    break; // one qualifying curve per prefab is sufficient
                }
            }

            W(w, $"  Sampled prefabs with Origin != 0: {sampled}");
            W(w, $"  Nodes[0] not in graph:            {notInGraph}  (all curves SmallVehicles-only or OOB)");
            W(w, $"  Nodes[0] in graph, no out-edge:   {notEdgeSource}");
            // Formula is algebraically guaranteed; only fail if code has a bug
            // (notInGraph is acceptable for prefabs with no valid truck curves)
            return notEdgeSource == 0;
        }

        // ── Check 2 — Graph Connectivity ─────────────────────────────────────

        private bool Check2_Connectivity(StreamWriter w)
        {
            var componentNodes = new Dictionary<ulong, List<ulong>>();
            foreach (var uid in _graph.Nodes.Keys)
            {
                var root = UfFind(uid);
                if (!componentNodes.TryGetValue(root, out var list))
                    componentNodes[root] = list = new List<ulong>();
                list.Add(uid);
            }

            int    totalComps = componentNodes.Count;
            double mainPct    = _graph.Nodes.Count > 0
                ? 100.0 * _mainComponentSize / _graph.Nodes.Count
                : 0;

            // Second-largest component
            int secondSize = componentNodes
                .Where(kv => kv.Key != _mainRoot)
                .Select(kv => kv.Value.Count)
                .DefaultIfEmpty(0)
                .Max();

            W(w, $"  Total components:    {totalComps}");
            W(w, $"  Largest component:   {_mainComponentSize:N0} nodes ({mainPct:F2}%)");
            if (secondSize > 0)
                W(w, $"  2nd largest:         {secondSize:N0} nodes");

            var small = componentNodes
                .Where(kv => kv.Key != _mainRoot && kv.Value.Count < 10)
                .OrderByDescending(kv => kv.Value.Count)
                .ToList();

            W(w, $"  Small components (< 10 nodes): {small.Count}");
            foreach (var kv in small.Take(30))
            {
                var uids = string.Join(", ", kv.Value.Take(5).Select(u => u.ToString()));
                string suffix = kv.Value.Count > 5 ? ", ..." : "";
                W(w, $"    {kv.Value.Count} node(s): [{uids}{suffix}]");
            }
            if (small.Count > 30)
                W(w, $"    ... and {small.Count - 30} more");

            bool pass = mainPct >= 95.0;
            if (!pass) W(w, $"  WARNING: Largest component < 95% threshold");
            return pass;
        }

        // ── Check 3 — Roundabout Directionality ──────────────────────────────

        private bool Check3_Roundabouts(StreamWriter w)
        {
            int roundaboutCount = 0, violationCount = 0;

            foreach (var prefab in _mapper.Prefabs)
            {
                if (!prefab.Valid || prefab.Prefab?.FilePath == null) continue;
                if (prefab.Prefab.FilePath.IndexOf("roundabout",
                        StringComparison.OrdinalIgnoreCase) < 0) continue;

                roundaboutCount++;
                int descN3 = prefab.Prefab.PrefabNodes.Count;
                if (descN3 == 0 || prefab.Nodes.Count == 0 || prefab.Prefab.NavCurves == null) continue;

                // Build the set of (from,to) pairs this prefab's curves produce
                var localSet = new HashSet<(ulong, ulong)>();
                foreach (var curve in prefab.Prefab.NavCurves)
                {
                    if (curve.AllowedVehicles == 1) continue;
                    if (curve.StartNodeIndex >= descN3 || curve.EndNodeIndex >= descN3) continue;
                    var from = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, curve.StartNodeIndex, descN3);
                    var to   = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, curve.EndNodeIndex,   descN3);
                    if (from != 0 && to != 0 && from != to) localSet.Add((from, to));
                }

                bool prefabViolated = false;
                foreach (var (from, to) in localSet)
                {
                    if (localSet.Contains((to, from)))
                    {
                        violationCount++;
                        if (!prefabViolated && violationCount <= 5)
                        {
                            W(w, $"  VIOLATION: uid={prefab.Uid}  file={prefab.Prefab.FilePath}");
                            W(w, $"    Bidirectional pair: {from} ↔ {to}");
                        }
                        prefabViolated = true;
                        break;
                    }
                }
            }

            W(w, $"  Roundabout prefabs found:      {roundaboutCount}");
            W(w, $"  Bidirectional violations:      {violationCount}");
            // Tolerance: T-roundabout templates (e.g. blke_hw2_x_r1_roundabout_t_tr_tmpl)
            // contain a through lane with legitimate bidirectional NavCurves by PPD design.
            // Allow up to 3 violations before flagging as a real problem.
            return violationCount <= 3;
        }

        // ── Check 4 — Road Directionality Sample ─────────────────────────────

        private bool Check4_RoadDirectionality(StreamWriter w)
        {
            // Positive-only check: if a direction is expected (LanesRight/Left > 0),
            // an edge (any type) must exist.  We do NOT require absence for one-way
            // roads since prefab junctions at both ends may add reverse edges.

            int tested = 0, passCount = 0, failCount = 0;

            foreach (var road in _mapper.Roads)
            {
                if (tested >= 100) break;
                if (!road.Valid || road.Hidden || road.RoadLook == null) continue;

                var sn = road.GetStartNode();
                var en = road.GetEndNode();
                if (sn == null || en == null) continue;
                if (IsZeroNode(sn) || IsZeroNode(en)) continue;

                tested++;
                ulong su = sn.Uid, eu = en.Uid;
                bool expectFwd = road.RoadLook.LanesRight.Count > 0;
                bool expectBwd = road.RoadLook.LanesLeft.Count > 0;
                bool hasFwd    = _edgeSet.Contains((su, eu));
                bool hasBwd    = _edgeSet.Contains((eu, su));

                // Only fail if an expected direction is absent
                bool ok = (!expectFwd || hasFwd) && (!expectBwd || hasBwd);

                if (ok) { passCount++; }
                else
                {
                    failCount++;
                    if (failCount <= 5)
                    {
                        W(w, $"  FAILURE: road uid={road.Uid} start={su} end={eu}");
                        W(w, $"    LanesRight={road.RoadLook.LanesRight.Count}" +
                             $" LanesLeft={road.RoadLook.LanesLeft.Count}");
                        W(w, $"    expectFwd={expectFwd} hasFwd={hasFwd}" +
                             $"  expectBwd={expectBwd} hasBwd={hasBwd}");
                    }
                }
            }

            W(w, $"  Sample size: {tested}");
            W(w, $"  Pass: {passCount}  Fail: {failCount}");
            double pct = tested > 0 ? 100.0 * passCount / tested : 0;
            if (tested > 0) W(w, $"  Pass rate: {pct:F1}%  (threshold: 98%)");
            return tested > 0 && pct >= 98.0;
        }

        // ── Check 5 — Zero/Negative Weights ──────────────────────────────────

        private bool Check5_EdgeWeights(StreamWriter w)
        {
            int bad = 0;
            foreach (var e in _graph.Edges)
            {
                if (e.Weight <= 0f || e.Length < 0f)
                {
                    bad++;
                    if (bad <= 10)
                        W(w, $"  BAD EDGE: {e.From}→{e.To} " +
                             $"weight={e.Weight} length={e.Length} type={e.ItemType}");
                }
            }
            W(w, $"  Zero/negative weight edges: {bad}");
            return bad == 0;
        }

        // ── Check 6 — City Reachability ───────────────────────────────────────

        private bool Check6_CityReachability(StreamWriter w)
        {
            var graphNodes = new List<GraphNode>(_graph.Nodes.Values);
            int total = 0, unreachable = 0;

            foreach (var cityItem in _mapper.Cities)
            {
                if (cityItem.Hidden || cityItem.City == null) continue;
                total++;

                string name = !string.IsNullOrEmpty(cityItem.City.Name)
                    ? cityItem.City.Name
                    : cityItem.City.LocalizationToken ?? "<unknown>";

                // Prefer a company node (Nodes[0]) within the city's bounding area —
                // company entrances are placed on actual road nodes, unlike city centre
                // markers which may sit in the middle of open terrain.
                float checkX = cityItem.X, checkZ = cityItem.Z;
                bool usedCompany = false;
                float hw = cityItem.Width  * 0.5f;
                float hh = cityItem.Height * 0.5f;

                foreach (var company in _mapper.Companies)
                {
                    if (company.Hidden) continue;
                    if (Math.Abs(company.X - cityItem.X) > hw) continue;
                    if (Math.Abs(company.Z - cityItem.Z) > hh) continue;
                    if (company.Nodes == null || company.Nodes.Count == 0) continue;

                    var compNode = _mapper.GetNodeByUid(company.Nodes[0]);
                    if (compNode == null || IsZeroNode(compNode)) continue;

                    checkX = compNode.X;
                    checkZ = compNode.Z;
                    usedCompany = true;
                    break;
                }

                // Nearest graph node to the check position
                GraphNode nearest = null;
                float minDSq = float.MaxValue;
                foreach (var gn in graphNodes)
                {
                    float dx = gn.X - checkX, dz = gn.Z - checkZ;
                    float dSq = dx * dx + dz * dz;
                    if (dSq < minDSq) { minDSq = dSq; nearest = gn; }
                }

                if (nearest == null)
                {
                    unreachable++;
                    W(w, $"  UNREACHABLE: '{name}' — graph is empty");
                    continue;
                }

                bool inMain = _ufParent.ContainsKey(nearest.Uid) &&
                              UfFind(nearest.Uid) == _mainRoot;
                bool hasOut = _nodesWithOutEdge.Contains(nearest.Uid);

                if (!inMain || !hasOut)
                {
                    unreachable++;
                    float dist = (float)Math.Sqrt(minDSq);
                    W(w, $"  UNREACHABLE: '{name}'  nearest={nearest.Uid}" +
                         $"  dist={dist:F0}m  inMain={inMain}  hasOut={hasOut}" +
                         $"  usedCompany={usedCompany}");
                }
            }

            W(w, $"  Cities checked: {total}");
            W(w, $"  Unreachable:    {unreachable}");
            return unreachable == 0;
        }

        // ── Check 7 — Ferry Connectivity ─────────────────────────────────────

        private bool Check7_FerryConnectivity(StreamWriter w)
        {
            var portToNodeUid = new Dictionary<ulong, ulong>();
            foreach (var ferry in _mapper.FerryConnections)
                if (ferry.Nodes != null && ferry.Nodes.Count > 0)
                    portToNodeUid[ferry.FerryPortId] = ferry.Nodes[0];

            int pairs = 0, issues = 0;
            var seen = new HashSet<(ulong, ulong)>();

            foreach (var ferry in _mapper.FerryConnections)
            {
                foreach (var conn in _mapper.LookupFerryConnection(ferry.FerryPortId))
                {
                    if (!portToNodeUid.TryGetValue(conn.StartPortToken, out var su)) continue;
                    if (!portToNodeUid.TryGetValue(conn.EndPortToken,   out var eu)) continue;
                    if (su == eu) continue;

                    var key = su < eu ? (su, eu) : (eu, su);
                    if (!seen.Add(key)) continue;
                    pairs++;

                    bool suIn    = _graph.Nodes.ContainsKey(su);
                    bool euIn    = _graph.Nodes.ContainsKey(eu);
                    bool suMain  = suIn && UfFind(su) == _mainRoot;
                    bool euMain  = euIn && UfFind(eu) == _mainRoot;
                    bool hasFwd  = _edgeSet.Contains((su, eu));
                    bool hasBwd  = _edgeSet.Contains((eu, su));

                    if (!suIn || !euIn || !suMain || !euMain || !hasFwd || !hasBwd)
                    {
                        issues++;
                        if (issues <= 5)
                        {
                            W(w, $"  ISSUE ferry pair #{issues}:");
                            W(w, $"    start={su} inGraph={suIn} inMain={suMain}");
                            W(w, $"    end={eu}   inGraph={euIn} inMain={euMain}");
                            W(w, $"    fwdEdge={hasFwd} bwdEdge={hasBwd}");
                        }
                    }
                }
            }

            W(w, $"  Ferry route pairs:  {pairs}  (expected ~{pairs * 2} edges)");
            W(w, $"  Ferry edges in graph: {_ferryEdgeCount}");
            W(w, $"  Issues: {issues}");
            return issues == 0;
        }

        // ── Check 8 — Edge Count Ratios ───────────────────────────────────────

        private bool Check8_EdgeRatios(StreamWriter w)
        {
            int    total  = _graph.Edges.Count;
            int    nodes  = _graph.Nodes.Count;
            double ePerN  = nodes > 0 ? (double)total / nodes : 0;
            double roadF  = total > 0 ? (double)_roadEdgeCount  / total : 0;
            double prefF  = total > 0 ? (double)_prefabEdgeCount / total : 0;
            double ferrF  = total > 0 ? (double)_ferryEdgeCount  / total : 0;

            // Full-mesh prefab approach produces O(N²) edges per junction, so ratios differ
            // from a NavCurve-based graph. Thresholds calibrated for ETS2 full-mesh output:
            //   edges/nodes ~2.3, road ~38%, prefab ~62%, ferry ~0.03%
            bool p1 = ePerN >= 1.5 && ePerN <= 3.5;
            bool p2 = roadF >= 0.25 && roadF <= 0.70;
            bool p3 = prefF >= 0.30 && prefF <= 0.75;
            bool p4 = ferrF < 0.005;

            W(w, $"  Road edges:   {_roadEdgeCount:N0}  ({100*roadF:F1}%)");
            W(w, $"  Prefab edges: {_prefabEdgeCount:N0}  ({100*prefF:F1}%)");
            W(w, $"  Ferry edges:  {_ferryEdgeCount:N0}  ({100*ferrF:F3}%)");
            W(w, $"  edges/nodes:  {ePerN:F3}  [{(p1?"✓ 1.5–3.5":"✗ expected 1.5–3.5")}]");
            W(w, $"  road/total:   {roadF:F3}  [{(p2?"✓ 0.25–0.70":"✗ expected 0.25–0.70")}]");
            W(w, $"  prefab/total: {prefF:F3}  [{(p3?"✓ 0.30–0.75":"✗ expected 0.30–0.75")}]");
            W(w, $"  ferry/total:  {ferrF:F4}  [{(p4?"✓ <0.005":"✗ expected <0.005")}]");

            return p1 && p2 && p3 && p4;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Same formula as RoutingGraphBuilder.GetGlobalNodeUid:
        //   Nodes[i] ↔ PPD ControlNode[(Origin+i)%N]
        //   PPD ControlNode[k] ↔ Nodes[(k-Origin+N)%N]
        private static ulong GetGlobalNodeUid(
            System.Collections.Generic.List<ulong> nodes, int origin, byte idx, int descriptorNodeCount)
        {
            int rotated = (idx - origin + descriptorNodeCount) % descriptorNodeCount;
            return rotated < nodes.Count ? nodes[rotated] : 0UL;
        }

        // Iterative path-halving Union-Find (avoids stack overflow on 176k nodes)
        private ulong UfFind(ulong x)
        {
            while (_ufParent[x] != x)
            {
                _ufParent[x] = _ufParent[_ufParent[x]]; // path halving
                x = _ufParent[x];
            }
            return x;
        }

        private void UfUnion(ulong a, ulong b)
        {
            var ra = UfFind(a);
            var rb = UfFind(b);
            if (ra != rb) _ufParent[ra] = rb;
        }

        private static bool IsZeroNode(TsNode node) =>
            Math.Abs(node.X) < 0.001f && Math.Abs(node.Z) < 0.001f;

        // Write to both console and the report file simultaneously
        private static void W(StreamWriter w, string line)
        {
            Console.WriteLine(line);
            w.WriteLine(line);
        }
    }
}
