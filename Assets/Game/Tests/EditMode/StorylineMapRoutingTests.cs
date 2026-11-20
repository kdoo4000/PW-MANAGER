using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Presentation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class StorylineMapRoutingTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AlignedGroupDetour_UsesALaneBeyondBothGroupFrames(bool vertical, bool reverse)
        {
            var from = new Rect(405, 1029, 120, 166); var to = new Rect(749, 1029, 120, 166);
            var first = new Rect(392, 48, 280, 1174); var second = new Rect(736, 48, 280, 1186);
            var obstacles = new[] { new Rect(541, 1029, 120, 166), new Rect(885, 1029, 120, 166) };
            Rect Rotate(Rect r) => vertical ? new Rect(r.y, r.x, r.height, r.width) : r;
            from = Rotate(from); to = Rotate(to); first = Rotate(first); second = Rotate(second); obstacles = obstacles.Select(Rotate).ToArray();
            if (reverse) { (from, to) = (to, from); (first, second) = (second, first); }
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), first, second, new[] { first, second });
            Assert.That(path, Has.Length.EqualTo(4)); AssertEntrySides(path, from, to);
            if (vertical) Assert.That(path[1].x, Is.EqualTo(path[2].x)); else Assert.That(path[1].y, Is.EqualTo(path[2].y));
            foreach (var frame in new[] { first, second })
            {
                Assert.That(StorylineMapRouting.CrossesInterior(path[1], path[2], frame), Is.False);
                Assert.That(vertical ? path[1].x - frame.xMax : path[1].y - frame.yMax, Is.GreaterThanOrEqualTo(24));
            }
            foreach (var segment in new[] { (path[0], path[1]), (path[2], path[3]) })
                Assert.That(Mathf.Abs(segment.Item2.x - segment.Item1.x), Is.EqualTo(Mathf.Abs(segment.Item2.y - segment.Item1.y)).Within(.01f));
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void LongGroupExit_UsesTwoDiagonalTurnsAndAnOuterStraightLane(bool horizontal, bool reverse)
        {
            var from = new Rect(61, 665, 120, 166); var to = new Rect(80, 1300, 120, 166);
            var frame = new Rect(48, 48, 280, 1174);
            var obstacles = new[] { new Rect(61, 847, 120, 166), new Rect(61, 1029, 120, 166), new Rect(197, 665, 120, 166), new Rect(197, 847, 120, 166), new Rect(197, 1029, 120, 166) };
            Rect Rotate(Rect r) => horizontal ? new Rect(r.y, r.x, r.height, r.width) : r;
            from = Rotate(from); to = Rotate(to); frame = Rotate(frame); obstacles = obstacles.Select(Rotate).ToArray();
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), reverse ? null : frame, reverse ? frame : null);
            Assert.That(path, Has.Length.EqualTo(4)); AssertEntrySides(path, from, to);
            foreach (var segment in new[] { (path[0], path[1]), (path[2], path[3]) })
                Assert.That(Mathf.Abs(segment.Item2.x - segment.Item1.x), Is.EqualTo(Mathf.Abs(segment.Item2.y - segment.Item1.y)).Within(.01f));
            if (horizontal) Assert.That(path[1].y, Is.EqualTo(path[2].y)); else Assert.That(path[1].x, Is.EqualTo(path[2].x));
            Assert.That(StorylineMapRouting.CrossesInterior(path[1], path[2], frame), Is.False);
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [TestCase(240, 170)]
        [TestCase(240, -170)]
        [TestCase(-240, 170)]
        [TestCase(-240, -170)]
        public void CrossingAClearCornerDiagonal_DoesNotChangeItsPortsOrAddTurns(int x, int y)
        {
            var from = new Rect(0, 0, 100, 120); var to = new Rect(x, y, 100, 120);
            var expected = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            var delta = expected[1] - expected[0]; var normal = new Vector2(-delta.y, delta.x).normalized;
            var middle = (expected[0] + expected[1]) / 2;
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), new[] { new[] { middle - normal * 20, middle + normal * 20 } });
            Assert.That(path, Is.EqualTo(expected)); Assert.That(path, Has.Length.EqualTo(2));
        }

        [Test]
        public void StaggeredObstacles_UseVisibilityCornersWithoutSmallZigzags()
        {
            var from = new Rect(0, 0, 80, 100); var to = new Rect(600, 0, 80, 100);
            var obstacles = new[] { new Rect(200, -80, 100, 240), new Rect(400, -160, 100, 240) };
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            Assert.That(path.Length, Is.InRange(3, 4)); AssertEntrySides(path, from, to);
            Assert.That(path.Zip(path.Skip(1), Vector2.Distance).Sum(), Is.LessThan(800));
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameRowBlockedByPortraits_UsesTopAtBothEnds(bool reverse)
        {
            var from = new Rect(48, 48, 120, 166); var to = new Rect(456, 48, 120, 166);
            var obstacles = new[] { new Rect(184, 48, 120, 166), new Rect(320, 48, 120, 166) };
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(4));
            Assert.That(path[0].y, Is.EqualTo(from.yMin)); Assert.That(path.Last().y, Is.EqualTo(to.yMin));
            Assert.That(path[1].y, Is.LessThan(from.yMin)); Assert.That(path[2].y, Is.LessThan(to.yMin));
            AssertSegments(path); AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [Test]
        public void SameRowRepeatedDetour_PrefersClearBottomSide()
        {
            var from = new Rect(48, 120, 120, 166); var to = new Rect(456, 120, 120, 166);
            var obstacles = new[] { new Rect(184, 120, 120, 166), new Rect(320, 120, 120, 166) };
            var first = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            var second = StorylineMapRouting.Route(from, to, obstacles, new[] { first });
            Assert.That(second[0].y, Is.EqualTo(from.yMax)); Assert.That(second.Last().y, Is.EqualTo(to.yMax));
            Assert.That(second[1].y, Is.GreaterThan(from.yMax));
            AssertSegments(second); AssertEntrySides(second, from, to);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LowerRowDetour_PrefersOutsideBottomOverGapBetweenRows(bool reverse)
        {
            var from = new Rect(48, 280, 120, 166); var to = new Rect(456, 280, 120, 166);
            var obstacles = new[] { new Rect(184, 280, 120, 166), new Rect(320, 280, 120, 166),
                new Rect(48, 48, 120, 166), new Rect(184, 48, 120, 166), new Rect(456, 48, 120, 166) };
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(4));
            Assert.That(path[0].y, Is.EqualTo(from.yMax)); Assert.That(path.Last().y, Is.EqualTo(to.yMax));
            Assert.That(path[1].y, Is.GreaterThan(from.yMax));
            AssertSegments(path); AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [Test]
        public void SameRowWithBlockedTop_FallsBackWithoutCrossingCards()
        {
            var from = new Rect(48, 120, 120, 166); var to = new Rect(456, 120, 120, 166);
            var obstacles = new[] { new Rect(184, 120, 256, 166), new Rect(0, -100, 640, 216) };
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            Assert.That(path.Length, Is.GreaterThan(1));
            AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++) foreach (var box in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        [Test]
        public void FacingBoxes_ConnectDirectlyWithoutBends()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(240, 20, 100, 120);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(path[0].x, Is.EqualTo(from.xMax)); Assert.That(path[1].x, Is.EqualTo(to.xMin));
            Assert.That(path[0].y, Is.EqualTo(path[1].y));
            AssertEntrySides(path, from, to);
        }

        [Test]
        public void VerticalBoxes_UseBottomAndTopPorts()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(20, 260, 100, 120);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(path[0].y, Is.EqualTo(from.yMax)); Assert.That(path[1].y, Is.EqualTo(to.yMin));
            Assert.That(path[0].x, Is.EqualTo(path[1].x));
            AssertEntrySides(path, from, to);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DifferentSizedBoxesWithOverlappingFaces_ShareAStraightConnection(bool vertical, bool reverse)
        {
            var from = new Rect(392, 48, 280, 446); var to = new Rect(736, 48, 416, 216);
            Rect Rotate(Rect rect) => vertical ? new Rect(rect.y, rect.x, rect.height, rect.width) : rect;
            from = Rotate(from); to = Rotate(to);
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2), "Overlapping faces should use a common coordinate instead of a diagonal dogleg.");
            AssertEntrySides(path, from, to);
            if (vertical) Assert.That(path[0].x, Is.EqualTo(path[1].x));
            else Assert.That(path[0].y, Is.EqualTo(path[1].y));
        }

        [Test]
        public void TouchingClearanceZones_NeverProduceNonFiniteEndpoints()
        {
            var path = StorylineMapRouting.Route(new Rect(0, 0, 100, 100), new Rect(108, 0, 100, 100), Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Is.Not.Empty);
            Assert.That(path.All(p => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y)), Is.True);
        }

        [Test]
        public void OffsetBoxes_ConnectFacingCornersWithOneStraightSegment()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(260, 240, 100, 120);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(path[0], Is.EqualTo(new Vector2(from.xMax, from.yMax)));
            Assert.That(path[1], Is.EqualTo(new Vector2(to.xMin, to.yMin)));
            AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++)
            {
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], from), Is.False);
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], to), Is.False);
            }
        }

        [Test]
        public void AlignedDiagonalBoxes_ConnectAt45DegreesWithoutBends()
        {
            var path = StorylineMapRouting.Route(new Rect(20, 20, 100, 100), new Rect(240, 240, 100, 100), Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(Mathf.Abs(path[1].x - path[0].x), Is.EqualTo(Mathf.Abs(path[1].y - path[0].y)).Within(.001));
        }

        [Test]
        public void AdjacentRows_ConnectFacingCornersWithoutBends()
        {
            var from = new Rect(196, 300, 120, 166); var to = new Rect(60, 114, 120, 166);
            var obstacles = new[] { new Rect(196, 114, 120, 166), new Rect(60, 300, 120, 166), new Rect(56, 87, 264, 25) };
            var existing = new[] { new Vector2(188, 788), new Vector2(188, 292) };
            var path = StorylineMapRouting.Route(from, to, obstacles, new[] { existing });
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(path[0], Is.EqualTo(new Vector2(from.xMin, from.yMin)));
            Assert.That(path[1], Is.EqualTo(new Vector2(to.xMax, to.yMax)));
            AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++)
            {
                foreach (var obstacle in obstacles.Concat(new[] { from, to }))
                    Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], obstacle), Is.False);
                Assert.That(SegmentsCross(path[i - 1], path[i], existing[0], existing[1]), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttachedTagLine_CrossingAnotherLineDoesNotAddBends(bool reverse)
        {
            var from = new Rect(61, 115, 120, 166); var to = new Rect(197, 301, 120, 166);
            if (reverse) (from, to) = (to, from);
            var obstacles = new[] { new Rect(197, 115, 120, 166), new Rect(61, 301, 120, 166), new Rect(57, 88, 264, 25) };
            var existing = new[] { new Vector2(189, 289), new Vector2(189, 788) };
            var path = StorylineMapRouting.Route(from, to, obstacles, new[] { existing });
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            foreach (var obstacle in obstacles) Assert.That(StorylineMapRouting.CrossesInterior(path[0], path[1], obstacle), Is.False);
        }

        [Test]
        public void Obstacle_IsAvoidedWithAShortDiagonalDetour()
        {
            var from = new Rect(20, 80, 100, 100); var to = new Rect(360, 80, 100, 100);
            var obstacle = new Rect(190, 30, 100, 200);
            var path = StorylineMapRouting.Route(from, to, new[] { obstacle }, Array.Empty<Vector2[]>());
            Assert.That(path.Length, Is.InRange(3, 6));
            AssertSegments(path);
            Assert.That(path.Zip(path.Skip(1), (a, b) => a.x != b.x && a.y != b.y).Any(x => x), Is.True);
            for (var i = 1; i < path.Length; i++)
            {
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], obstacle), Is.False);
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], from), Is.False);
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], to), Is.False);
            }
        }

        [TestCase(0, 0, 100, 100, true)]
        [TestCase(0, 100, 100, 0, true)]
        [TestCase(0, 0, 100, 30, false)]
        [TestCase(0, 0, 30, 100, false)]
        [TestCase(0, 80, 80, 0, false)]
        [TestCase(0, 40, 100, 40, false)]
        public void DiagonalIntersection_DetectsPortraitInteriorAndAllowsClearEdges(int ax, int ay, int bx, int by, bool crosses)
        {
            Assert.That(StorylineMapRouting.CrossesInterior(new Vector2(ax, ay), new Vector2(bx, by), new Rect(40, 40, 20, 20)), Is.EqualTo(crosses));
        }

        [Test]
        public void ParallelStories_UseDifferentPortsAndDoNotShareTheirLine()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(240, 20, 100, 120);
            var first = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            var second = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), new[] { first });
            Assert.That(second, Has.Length.EqualTo(2));
            Assert.That(Mathf.Abs(first[0].y - second[0].y), Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void IdenticalInput_ProducesIdenticalRoute()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(280, 230, 100, 120);
            var obstacles = new[] { new Rect(160, 110, 60, 120) };
            var first = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            var second = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>());
            Assert.That(first, Is.Not.Empty); Assert.That(second, Is.EqualTo(first));
            AssertSegments(first);
            Assert.That(first.Zip(first.Skip(1), Vector2.Distance).All(x => x > 0), Is.True);
        }

        [Test]
        public void ParallelDiagonalStories_StaySeparate()
        {
            var from = new Rect(20, 20, 100, 120); var to = new Rect(260, 240, 100, 120);
            var first = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            var second = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), new[] { first });
            Assert.That(first.Length, Is.InRange(2, 4)); Assert.That(second.Length, Is.InRange(2, 4));
            Assert.That(first, Has.Length.EqualTo(2)); AssertSegments(second);
            Assert.That(second, Is.Not.EqualTo(first));
            for (var i = 1; i < first.Length; i++)
            for (var j = 1; j < second.Length; j++)
                Assert.That(SegmentsCross(first[i - 1], first[i], second[j - 1], second[j]), Is.False);
        }

        [Test]
        public void CrossingAnExistingLine_KeepsTheClearVerticalConnectionStraight()
        {
            var existing = new[] { new Vector2(100, 200), new Vector2(200, 200) };
            var path = StorylineMapRouting.Route(new Rect(100, 20, 100, 80), new Rect(100, 300, 100, 80), Array.Empty<Rect>(), new[] { existing });
            Assert.That(path, Has.Length.EqualTo(2)); Assert.That(path[0].x, Is.EqualTo(path[1].x));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StableExit_PrefersAClearStraightPortPairOverAvoidingOtherLines(bool reverse)
        {
            var from = new Rect(61, 301, 120, 166); var to = new Rect(405, 483, 120, 166);
            var first = new Rect(48, 48, 280, 628); var second = new Rect(392, 48, 280, 628);
            var obstacles = new[] { new Rect(197, 301, 120, 166), new Rect(61, 483, 120, 166), new Rect(405, 301, 120, 166) };
            var internalLine = new[] { new Vector2(197, 467), new Vector2(181, 483) };
            if (reverse) { (from, to) = (to, from); (first, second) = (second, first); }
            var path = StorylineMapRouting.Route(from, to, obstacles, new[] { internalLine }, first, second, new[] { first, second });
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            Assert.That(Vector2.Distance(path[0], path[1]), Is.LessThan(400));
            foreach (var obstacle in obstacles.Concat(new[] { from, to })) Assert.That(StorylineMapRouting.CrossesInterior(path[0], path[1], obstacle), Is.False);
        }

        [Test]
        public void CrossingADiagonalLine_DoesNotCreateExtraCorners()
        {
            var existing = new[] { new Vector2(100, 140), new Vector2(200, 260) };
            var path = StorylineMapRouting.Route(new Rect(100, 20, 100, 80), new Rect(100, 300, 100, 80), Array.Empty<Rect>(), new[] { existing });
            Assert.That(path, Has.Length.EqualTo(2)); Assert.That(path[0].x, Is.EqualTo(path[1].x));
        }

        [TestCase(-280, 230)]
        [TestCase(280, -230)]
        [TestCase(-280, -230)]
        [TestCase(280.5f, 230.25f)]
        public void EveryDirectionAndFractionalPosition_ConnectsExactFacingCorners(float x, float y)
        {
            var from = new Rect(0, 0, 100, 120); var to = new Rect(x, y, 100, 120);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2));
            Assert.That(path[0], Is.EqualTo(new Vector2(x > 0 ? from.xMax : from.xMin, y > 0 ? from.yMax : from.yMin)));
            Assert.That(path[1], Is.EqualTo(new Vector2(x > 0 ? to.xMin : to.xMax, y > 0 ? to.yMin : to.yMax)));
            AssertEntrySides(path, from, to);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlockedFacingCorners_FindAnotherClearPortBeforeAddingTurns(bool reverse)
        {
            var from = new Rect(0, 0, 100, 120); var to = new Rect(300, 220, 100, 120);
            var obstacle = new Rect(180, 140, 40, 60);
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, new[] { obstacle }, Array.Empty<Vector2[]>());
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++) foreach (var box in new[] { from, to, obstacle })
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], box), Is.False);
        }

        private static void AssertSegments(Vector2[] path)
        {
            Assert.That(path.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(path.All(p => float.IsFinite(p.x) && float.IsFinite(p.y)), Is.True);
            for (var i = 1; i < path.Length; i++) Assert.That(Vector2.Distance(path[i - 1], path[i]), Is.GreaterThan(.01f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExternalGroupConnection_UsesFewBendsWithoutCrossingPortraits(bool tagTeam)
        {
            var parent = new Rect(48, 48, 280, 446);
            var from = tagTeam ? new Rect(55, 87, 268, 202) : new Rect(197, 115, 120, 166);
            var to = tagTeam ? new Rect(48, 788, 280, 216) : new Rect(197, 558, 120, 166);
            var obstacles = new[] { new Rect(61, 301, 120, 166), new Rect(197, 301, 120, 166), new Rect(392, 48, 280, 446) }.ToList();
            if (!tagTeam) obstacles.Add(new Rect(61, 115, 120, 166));
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), parent);
            Assert.That(path.Length, Is.InRange(2, 4)); AssertSegments(path); AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++)
            foreach (var obstacle in obstacles.Concat(new[] { from, to }))
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], obstacle), Is.False);
        }

        [Test]
        public void ShortClearExitFromAStable_RemainsStraight()
        {
            var from = new Rect(749, 87, 120, 166); var to = new Rect(749, 328, 120, 166);
            var path = StorylineMapRouting.Route(from, to, Array.Empty<Rect>(), Array.Empty<Vector2[]>(), new Rect(736, 48, 416, 216));
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            Assert.That(path[0].x, Is.EqualTo(path[1].x));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClearAxisExitFromAStable_UsesTheEmptySpaceInsideTheFrame(bool horizontal, bool reverse)
        {
            var from = new Rect(541, 301, 120, 166); var to = new Rect(541, 740, 120, 166);
            var parent = new Rect(392, 48, 280, 628);
            var obstacles = new[] { new Rect(405, 301, 120, 166), new Rect(405, 483, 120, 166) };
            Rect Rotate(Rect r) => horizontal ? new Rect(r.y, r.x, r.height, r.width) : r;
            from = Rotate(from); to = Rotate(to); parent = Rotate(parent); obstacles = obstacles.Select(Rotate).ToArray();
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), reverse ? null : parent, reverse ? parent : null, new[] { parent });
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            if (horizontal) Assert.That(path[0].y, Is.EqualTo(path[1].y)); else Assert.That(path[0].x, Is.EqualTo(path[1].x));
            foreach (var obstacle in obstacles) Assert.That(StorylineMapRouting.CrossesInterior(path[0], path[1], obstacle), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShortInternalConnection_UsesClearFacingCornersInsideFrames(bool reverse)
        {
            var from = new Rect(749, 115, 120, 166); var to = new Rect(885, 301, 120, 166);
            if (reverse) (from, to) = (to, from);
            var obstacles = new[] { new Rect(885, 115, 120, 166), new Rect(749, 301, 120, 166), new Rect(745, 88, 264, 25) };
            var frames = new[] { new Rect(736, 48, 280, 446), new Rect(743, 87, 268, 202) };
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), frames: frames);
            Assert.That(path, Has.Length.EqualTo(2)); AssertEntrySides(path, from, to);
            foreach (var obstacle in obstacles) Assert.That(StorylineMapRouting.CrossesInterior(path[0], path[1], obstacle), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PassingBelowTeams_UsesASimpleLaneOutsidePortraitsAndHeaders(bool reverse)
        {
            var from = new Rect(60, 100, 120, 166); var to = new Rect(540, 100, 120, 166);
            if (reverse) (from, to) = (to, from);
            var obstacles = new[] { new Rect(196, 100, 120, 166), new Rect(404, 100, 120, 166),
                new Rect(52, 62, 272, 32), new Rect(396, 62, 272, 32) };
            var frames = new[] { new Rect(48, 60, 280, 216), new Rect(392, 60, 280, 216) };
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), frames: frames);
            Assert.That(path, Is.Not.Empty); AssertSegments(path); AssertEntrySides(path, from, to);
            var horizontal = path.Zip(path.Skip(1), (a, b) => (a, b))
                .Where(s => Mathf.Abs(s.a.y - s.b.y) < .001f && Mathf.Abs(s.a.x - s.b.x) > 100).ToArray();
            Assert.That(horizontal, Is.Not.Empty);
            foreach (var segment in horizontal)
                Assert.That(segment.a.y <= 58 || segment.a.y >= 270, Is.True, "The lane must clear the portraits and headers.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlockedInternalColumn_UsesASimpleSideLane(bool reverse)
        {
            var from = new Rect(60, 100, 120, 166); var to = new Rect(60, 500, 120, 166);
            if (reverse) (from, to) = (to, from);
            var obstacles = new[] { new Rect(60, 300, 120, 166), new Rect(196, 100, 120, 166),
                new Rect(196, 300, 120, 166), new Rect(196, 500, 120, 166), new Rect(52, 62, 272, 32) };
            var parent = new Rect(48, 48, 280, 630);
            var path = StorylineMapRouting.Route(from, to, obstacles, Array.Empty<Vector2[]>(), frames: new[] { parent });
            Assert.That(path, Is.Not.Empty); AssertSegments(path); AssertEntrySides(path, from, to);
            Assert.That(path.Length, Is.LessThanOrEqualTo(4));
            var vertical = path.Zip(path.Skip(1), (a, b) => (a, b))
                .Where(s => Mathf.Abs(s.a.x - s.b.x) < .001f && Mathf.Abs(s.a.y - s.b.y) > 100).ToArray();
            Assert.That(vertical, Is.Not.Empty);
            foreach (var segment in vertical)
                Assert.That(segment.a.x, Is.LessThanOrEqualTo(56), "The side lane must clear the blocking portrait.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ObstructedRowsAndColumns_NeverAttachTangentiallyToABoxFace(bool horizontal, bool reverse)
        {
            var from = new Rect(100, 20, 100, 80); var to = new Rect(100, 300, 100, 80); var obstacle = new Rect(100, 140, 100, 100);
            Rect Rotate(Rect rect) => horizontal ? new Rect(rect.y, rect.x, rect.height, rect.width) : rect;
            from = Rotate(from); to = Rotate(to); obstacle = Rotate(obstacle);
            if (reverse) (from, to) = (to, from);
            var path = StorylineMapRouting.Route(from, to, new[] { obstacle }, Array.Empty<Vector2[]>());
            Assert.That(path, Is.Not.Empty); AssertSegments(path); AssertEntrySides(path, from, to);
            for (var i = 1; i < path.Length; i++)
                Assert.That(StorylineMapRouting.CrossesInterior(path[i - 1], path[i], obstacle), Is.False);
        }

        private static void AssertEntrySides(Vector2[] path, Rect from, Rect to)
        {
            void Check(Vector2 anchor, Vector2 outside, Rect rect)
            {
                if (Mathf.Abs(anchor.x - rect.xMax) < .001f) Assert.That(outside.x, Is.GreaterThan(anchor.x), "A right face requires horizontal movement out of the box.");
                else if (Mathf.Abs(anchor.x - rect.xMin) < .001f) Assert.That(outside.x, Is.LessThan(anchor.x), "A left face requires horizontal movement out of the box.");
                else if (Mathf.Abs(anchor.y - rect.yMax) < .001f) Assert.That(outside.y, Is.GreaterThan(anchor.y), "A bottom face requires vertical movement out of the box.");
                else if (Mathf.Abs(anchor.y - rect.yMin) < .001f) Assert.That(outside.y, Is.LessThan(anchor.y), "A top face requires vertical movement out of the box.");
                else Assert.Fail("The endpoint must attach to a box face.");
            }
            Check(path[0], path[1], from);
            Check(path[path.Length - 1], path[path.Length - 2], to);
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }
    }
}
