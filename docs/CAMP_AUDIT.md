# Camp audit (Castle Ravenhold)

Started 2026-11-07 after the user: "your work on the spawn area is very very sloppy ... fix the placements of
everything", "please fix every little detail, nothing should seem out of place", "take more angles please",
"whatever they see make sure we have that data for future, for instance the carriage horse needs updating".

## How to look at the camp

`Deepbound.exe -batchmode -camptour <ABS dir> -port 7813` (NOT -nographics; no window, safe while someone plays).
Writes, with no HUD:
- `plan_all.png` (2048 px = 82 m, origin at the centre pixel, north up, ~25 px/m) and `plan_NW/NE/SW/SE.png`
  (2048 px = 44.1 m, centred at +-17.1 m).
- `air_*` aerial obliques (8 high from outside the wall, 8 lower and closer).
- `walk_<x>_<z>_<N|E|S|W>.png`: head-height views every 8 m across the courtyard, four headings each.
- `eye_*_<station>_fromN/E/S/W/a/top.png`: every station from all four sides and above; `wallNN_in/along`.
- `tour.txt`: station positions relative to the origin.
- `fire_<side>_<stand|sit>`: from the spawn fire's log ring toward each side; `tent_inside_up/out/table`; `blackjack_seat/hit` (seated at the card table with a hand dealt).
- **The inspector bot**: `-camptour <dir> -tourbot` (Networking/CampInspector) stands where a player uses each station, renders with the game's own light (plus the light players carry), and writes `inspect.txt`: DARK/DIM (measured brightness), NO-ROOM-TO-STAND, BLOCKED-BY, FRONT-INTO, FACES-AWAY, CROWDED; contact sheets `sheet_NN.jpg` (use view | approach), `faces_NN.jpg` (every named person), `round_<who>_walking/there.jpg` (rounds fast-forwarded). Run it after every camp change; it takes about two minutes.
- Add `-tourquick` to skip the walk grid and the station views (about a minute instead of ten). Output is JPG now.

Layout code: `Assets/Scripts/World/Lobby.cs` (positions use `S(x, y, z)` = x and z times `Spread` 1.35 from
`Origin`; the curtain wall is at radius 35 m; the gate is north).

## Findings and their state

Every finding is listed here with what was done, so nothing seen is lost. Open items stay open until fixed and
checked on a fresh tour.

### Open (beyond placement)
- [~] The carriage horse needs updating (the user, 2026-11-07). REBUILT 2026-11-07 (`World/CarriageRide.cs` BuildHorse/AnimateHorse: spine, ribcage, pelvis, jointed legs with a diagonal trot, skull with a hinged jaw and glowing sockets, harness, idle head toss / stamp / jaw chatter / tail swish). To check on a tour: hooves may float ~6 cm at full stride, knee/hock bend directions, bit vs reins during a head toss, hind legs clear of the shafts.

## Round 5 (2026-11-08, new camp stations, checked on `-talkshots` renders)

- [x] Brann stood by the tavern, 28 m from his own yard: `Lobby.YardSpot` was declared before `YardShift`, so it read the shift as zero (static fields initialise in order). Swapped; Brann, his stool, sticks, the Drills sign, his lamp post, the Squad Standard and the Warden's jaw now sit at the yard.
- [x] The first knife-board spot (worked out from an old plan) put the board against the tavern door. Moved to `KnifeSpot` = YardSpot + (1.8, 0, -4.8), throwing south along the yard fence with Brann behind you.
- [x] The well line: rope from the crossbeam to the water, a red float that ducks on a bite (`pastime_1/2`).
- To check on the next renders: the knife lane (`pastime_4-8`), the bell frame south-east of the shrine (`bells_1-2`), Brann's barrel (`wrestle_1-3`), Old Wick on his stool (`casino_8_old_wick`), rain and mist (`weather_1-3`), the pet tricks (`pet_1-4`, `camp_9b_pet_asleep`).

## Round 4 (2026-11-08, second inspector pass, read every sheet by eye)

- [x] The spawn fire was a tripod brazier standing in an empty stone ring: now `Props.Bonfire` (seven leaned split logs, charred tips, two burning across an ember bed, ash, one big flame with a 15/18 m shadowed light and three flickering side licks `Art/FlameLick`).
- [x] `MeshKit.Cylinder` grows UP from its base, not from its middle. Lying cylinders built as if centred were off by half a length: every fire log sat 0.9 m along the ring from where `LogSeats()` puts you (one seat per log was on air), the carriage axles stuck 1.2 m out of one side, the tavern kegs' hoops floated beside them. All three fixed; other sideways cylinders in camp code already start from one end or are too small to see.
- [x] Hall of records: the white marble face under a road brazier rendered as a blank lit panel (0.595): grey-tinted, candles unlit, lamp post further off (0.366, lines readable).
- [x] Oak leaves were near-black balls at night (one hung behind Brann's head in every yard shot): lighter greens. The gate oak grew into the gatehouse wall: removed.
- Open: `fire_logs` now flags BLOCKED-BY the bonfire's own collider (the station point is the fire itself: benign).

## Round 3 (2026-11-07 night, the inspector bot + the user's answers: warm and well lit; NPCs face you, work, wander and chat, better faces; signs AND names)

- [x] DARK range (0.053 -> 0.125): posts at both ends of the lanes and a line of three lanterns over them. Mannequin 0.067 -> 0.115 (still DIM by the measure, readable). Every road lamp lit and brighter, stall lamps 2.6, station lamps 2.6/9 m, warmer ambient.
- [x] Hobb stood with his back to his customers: now behind the anvil facing the counter (`Lobby.HobbSpot`), hammering it (sparks, a clang) while nobody is at the counter.
- [x] Aldric knelt square in front of the altar (BLOCKED-BY): now kneels beside it (`AldricSpot`), the altar clear.
- [x] Vey stood 1.8 m back from her counter: now right behind it (`VeySpot`).
- [x] Silas stood 3.5 m behind an empty stall counter: the stall is now a frame round him and his own table (`Stall(..., counter: false)`).
- [x] The hall of records had a road brazier right in front of its slab: the bounty board and records moved 1.5 m west.
- [x] Faces were nearly black (skin on the dark leather texture): skin is on the pale bone base now; bigger eyes with a glint, cheeks, a fuller mouth. Outfits: Hobb's bracers, apron straps, hair; Vey's fringe, shawl and pincushion; Brann's tabard; Corwin's scarf.
- [x] NPCs turn their body to you within 4.5 m (`FolkIdle.TurnsBody`), stop working and look at you within 3.8 m, greet you by name and class once a visit (`CampChatter.Greeting`), and show NAME / TRADE over their heads within 11 m when not talking.
- [x] Work: Hobb hammers, Vey sews, Brann drills, Ysolde traces runes, Marta wipes the bar (`FolkIdle.WorkKind`, arm pivots from `Props.GroupArm`).
- [x] Rounds (`World/FolkRounds`): Hobb to the well, Vey to Silas (he answers), Brann to the fire, Ysolde to the gong, Silas to the tavern door; server clock, grid path round the stalls, the struck-body walks with the figure. Smoke "camp folk rounds".
- [x] Signs (`World/SignArt`): Merchant, Smith, Tailor, Drills, Runes, Shrine, Tavern, Games, Bounties, Might, Archery.
- Open: false positives in the inspector (NO-ROOM at the omen stone, Corwin's box, Marta's bar); while someone is on rounds their shop still opens at the stall.

## Round 2 (2026-11-07 evening, the user's notes; APPLIED and checked on `-tourquick` renders)

- [x] "move the tavern to a place where it's not blocked by the gambling tent" and "place the tavern closer to the camp fire and logs, as well as the gambling tent": the tavern is now WEST of the spawn fire (`TavernCenter` origin + (-12.6, 0, -19)), its door facing the logs; the tent is EAST of it (`TentCenter` + (12.6, 0, -19)), open to the WEST (the old panel loop actually left the tent open to the NORTH). Dice, coffer, slots and the card table were mirrored inside it. The training yard moved to the tavern's old ground in the north-west (`Lobby.YardShift`).
- [x] "tent roof is see through when looking up": a second lathe (`TentRoofInside`) for the underside; checked on `tent_inside_up`.
- [x] "remove badly placed plant pots": the flower planters along the trade row and the stone planters round the bonfire are gone.
- [x] "remove badly placed carriage in the tent": the hand cart is gone.
- [x] "remove badly placed tree that's missing its trunk": the yew (its trunk hid under the cone's skirt) is gone; the two oaks by the fire went to make room.
- [x] "allow the players to sit around the camp fire at spawn on the logs": six logs laid ALONG the ring (they were spokes), two places each; F sits (`Player/CampSeat`, the Sit gesture for everyone), F / jump / a step stands. The plank bench ring is gone. Smoke "fire logs".
- [x] "MORE LIGHT SOURCES ... AROUND THINGS WHERE THE PLAYER NEEDS TO SEE OR GO": `Lobby.LampPost` + `BuildStationLamps`: lanterns either side of the tavern door, posts at the tent mouth, the tailor, the yard sergeant, the gong, the omen stone and the hall of records; a hanging lamp over the card table.
- Round 1 north-zone items about the tent, cart, tavern door and the tavern yard are superseded by the move; the rest of Round 1 is still open.

## Round 1 audit (2026-11-07, four zone reviewers on the first tour; NOT YET APPLIED)

Each finding has the evidence image, the code location and a proposed fix. Apply, re-run `-camptour`, tick off.

### Zone: north

Sixteen placement problems in the north zone, plus smaller ones. Two root causes drive most of it. The tavern door opens onto the back of the gaming tent. The bounty board, records slab, lamp, brazier and tent were all packed into a 2.7 m strip beside the road, while a large void sits unused north-west of the tent.

All coordinates are metres (x east, z north) from the camp origin. Lines are in `Assets/Scripts/World/Lobby.cs` unless another file is named. I did not edit anything. I did not recheck the south and east-side items I noted in passing; they are flagged for the other zones at the end.

## A. Tavern yard, gaming tent, cart

**1. The tavern door opens onto tent canvas, 1.3 m away (major).**
- The door is on the east wall at (-17.6, 16.2). The tent is centred at (-11.88, 17.82) with canvas radius 4.2, so its canvas sits at x ≈ -15.8 at z 16.2 and its roof edge at -16.4.
- Evidence: `plan_NW` (tavern roof edge at x -17.2 nearly touches the red tent disc), `eye_34_bounty_b` (tavern wall and tent canvas touching), `eye_71_wall315_in`.
- Code: line 35, `TentCenter = Origin + S(-8.8f, 0f, 13.2f)`.
- Fix: `TentCenter = Origin + S(-7.5f, 0f, 13.2f)`, which puts it at (-10.13, 17.82). That leaves about 3.4 m of clear yard in front of the door.
- I checked the new position against the lamp at x -4.6, the brazier at (-4.32, 20.25), the rope pole at (-5.54, 12.15) and the oak at (-16.2, 6.5). All clear. Dice, coffer, slots and blackjack follow the tent automatically.

**2. The hand cart is inside the tent and overlaps the coffer.**
- Cart at S(-7,14) = (-9.45, 18.9), rotated 35 degrees. The coffer is at (-10.28, 19.42), about 0.8 m from the cart's centre, so it lies inside the cart's footprint. The cart is also inside the tent mouth.
- Evidence: `plan_NW` (the red roof hides it), `eye_17_tent_top`.
- Code: line 507, `var cart = o + S(-7f, 0f, 14f);`
- Fix: `var cart = o + S(-8.2f, 0f, 20.6f);`, which is (-11.07, 27.8) in the empty void north of the tent. Keep the 35 degree yaw. It is 1.2 m clear of the new board position.

**3. Tent roof is one-sided: from inside you see open sky and a bare pole.**
- The `Lathe` is wound bottom to top, so only its outer face renders (see the comment at `MeshKit.cs` line 89).
- Evidence: `eye_15_tent_a` and `eye_16_tent_b` show sky above the walls.
- Code: lines 727-731.
- Fix: add a second part right after line 731:
  ```csharp
  var roofIn = MeshKit.Part(lights, "TentRoofIn", MeshKit.Lathe("tentRoofIn", new[]{ new Vector2(0f,2.6f), new Vector2(2.2f,1.4f), new Vector2(4.6f,0f) }, 16), Mat.Cloth, c + Vector3.up * 2.6f);
  ArtLibrary.Tint(roofIn.GetComponent<Renderer>(), new Color(0.4f, 0.07f, 0.09f));
  ```

**4. Tent walls are separate slabs with 0.27 m gaps.**
- 14 panels over 270 degrees are 1.25 m wide, but the spacing is about 1.51 m. The `eye_15` and `eye_16` views show light and the interior through the gaps.
- Code: line 723, `MeshKit.Box(new Vector3(1.25f, 2.6f, 0.06f))`.
- Fix: change the width to `1.58f`.

**5. Pennant floats 0.2 m above the pole.**
- The pole top is at y = 5.2 and the pennant spans y 5.4 to 5.8. Seen in `eye_17_tent_top` and `eye_72_wall315_along`.
- Code: line 732, `d.AddBox(c + Vector3.up * 2.6f, new Vector3(0.18f, 5.2f, 0.18f), ...)`.
- Fix: `d.AddBox(c + Vector3.up * 3.0f, new Vector3(0.18f, 6.0f, 0.18f), Mat.Wood);`

**6. Blackjack stool 0.18 m from the dice table.**
- Blackjack is at tent-relative (-0.6, -2.5), with stools at z -1.55 and x up to +0.36. The dice table is at (1.4, -1.2), radius 0.75. Both are cramped against the entrance.
- Code: line 41, `BlackjackSpot = TentCenter + new Vector3(-0.6f, 0f, -2.5f)`.
- Fix: `new Vector3(-1.0f, 0f, -2.5f)`. This leaves a 0.56 m gap to the dice table. The dealer stays 0.4 m inside the canvas, and the candle at (-2.6, -1.4) is still 0.64 m from the nearest stool.

**7. Tavern table 3 runs into the stage; table 2's stool stands in the doorway; a stool sits on the walk to the bar.**
- Tavern-relative. The stage is x -0.2 to 3.0, z -3.35 to -1.75. Table 3 at (-0.4, -1.6) has radius 0.6, so the stage corner is inside the tabletop.
- Table 2 at (2.4, 1.6) puts a stool at (2.26, 0.66), inside the door line (door z ±1.1 at x 4).
- Table 1 puts a stool at (-0.04, -0.04), right on the line from the door to the bar.
- Code: line 53 (`TavernBandSpot ... new Vector3(1.4f, 0f, -1.6f)`), line 820 (stage width `3.2f`), lines 821-830 (tables and stools).
- Fix:
  - Line 53 becomes `new Vector3(1.7f, 0f, -1.6f)`, and line 820 becomes `new Vector3(2.6f, 0.25f, 1.6f)`. The stage then spans x 0.4 to 3.0 and the musicians at 0.75, 1.7 and 2.65 still fit.
  - Replace the `foreach` at 821 with tables carrying explicit stool angles, so all stools stay out of the walkway and the doorway:
    ```csharp
    foreach (var (at, angs) in new[] {
        (c + new Vector3(0.1f,0f,0.9f),  new[]{0.67f,1.57f,2.47f}),
        (c + new Vector3(2.5f,0f,2.2f),  new[]{-0.4f,2.2f,3.9f}),
        (c + new Vector3(-0.4f,0f,-1.6f), new[]{1.57f,3.14f,4.71f}) })
    ```
  - In the stool loop (lines 825-830), use `foreach (float a in angs)` instead of `k*120f*Mathf.Deg2Rad+0.4f`.
  - I checked that none of the new stools hit the hearth jamb (0.27 m clear), the east wall or the counter.

**8. Tavern chimney stack sits on the eave and juts into the room.**
- It spans z 3.0 to 3.8 against a wall face at 3.3, so it pokes 0.3 m into the room under the roof, and its outer face sits right at the eave line. Seen in `plan_NW` at (-21.0, 19.6).
- Code: lines 791-792.
- Fix: `s.AddBox(c + new Vector3(0.6f, wallH + rise*0.5f + 0.5f, hz + 0.1f), new Vector3(1f, rise + 1.2f, 0.7f), Mat.StoneWall);` and `TavernChimney = c + new Vector3(0.6f, wallH + rise + 1.3f, hz + 0.1f);`

**9. Oak crown 1.1 m from the tavern's south-east eave.**
- The oak at S(-12.6, 6.6) = (-17.0, 8.9), scale 1.25, has a crown reach of about 3.2 m. The roof edge is at z 12.2. See `plan_NW`.
- Code: line 106, `(-12.6f, 6.6f, 1.25f)`.
- Fix: `(-12.0f, 4.8f, 1.25f)`, which is (-16.2, 6.5) and gives about 3.5 m clearance.

**10. Tavern window panes read as flat cream rectangles, and exist only on the outside faces.**
- Seen in `eye_21` and `eye_22`. The panes sit on the outer face of a solid wall.
- Code: line 843, `new Color(1f,0.6f,0.25f) * 1.3f`.
- Fix: reduce to `* 0.75f`. Low priority.

## B. Bounty board, Hall of Records, lamps and braziers

**11. A lamp post stands 1.3 m in front of the middle bounty notice, and a brazier sits 1.3 m in front of the records slab.**
- Board at S(-4.6, 17.2) = (-6.21, 23.22), facing east. Lamp foot at (-4.6, 23.0). Slab at (-5.61, 20.04). Brazier at (-4.32, 20.25).
- The 2.7 m strip between the tent roof edge (-7.3) and the road furniture holds the board, slab, lamp, brazier and trough, and the oak crown overhangs it.
- Evidence: `plan_NW` (lamp tick level with the board plank, brazier beside the slab), `eye_35_bounty_top`, `eye_36_records_a`, `eye_33_bounty_a`.
- Code: line 82 (`BountySpot = Origin + S(-4.6f, 0f, 17.2f)`) and line 84 (`RecordsSpot = BountySpot + new Vector3(0.6f, 0f, -3.1f)`).
- Fix: `BountySpot = Origin + S(-6.2f, 0f, 19.6f)`, which is (-8.37, 26.46). The records offset stays `(0.6f, 0f, -3.1f)`, giving the slab at (-7.77, 23.36).
  - Both still face east (`BountyFacing` is unchanged) and use the empty north-west void.
  - Clearances: slab to tent roof 1.4 m (after the tent moves), slab to lamp 3.4 m, board to trough 1.1 m.

**12. Bounty notices float 0.17 m in front of the board.**
- The board's front face is at x offset -0.07. Notices are spawned at `+BountyFacing * 0.12`, so their centre is at +0.12.
- Code: `DungeonDirector.cs` line 715, `Lobby.BountySpot + Lobby.BountyFacing * 0.12f + ...`
- Fix: `* -0.05f`. The papers then sit flush on the panel.

**13. The BountySign floats, and the bounty lamp is a light with no fixture.**
- The sign at y 2.42 is unsupported between the panel top (2.2) and the roof underside (about 2.65). The `BountyLamp` at bs + (1.2, 2.4, 0) has no mesh.
- Code: line 668 (sign) and lines 670-676 (lamp).
- Fix, sign: position `bs + new Vector3(-0.04f, 2.5f, 0f)`, so it touches the roof and sits on the panel face.
- Fix, lamp: replace lines 670-676 with `Props.WallTorch(lights, bs + new Vector3(-0.02f, 2.0f, 1.5f), Vector3.right, false, Fire);`, a torch mounted on the south post.

**14. Records slab candles have no candle body.**
- The flames at y 0.48 sit directly on the plinth top at 0.4 (`eye_33_bounty_a`).
- Code: lines 685-686.
- Fix: add a candle stub, e.g. `d.AddBox(rs + new Vector3(0.1f, 0.46f, z), new Vector3(0.07f, 0.12f, 0.07f), Mat.Bone);`, and raise the flame y to 0.58.

**15. Every lamp post shows a plain grey cube instead of a glowing lantern.**
- The glass box (0.2 x 0.28 x 0.2) at line 463 sits entirely inside the steel box (0.26 x 0.36 x 0.26) at line 462, so it is invisible.
- The lantern also hangs 0.12 m below its arm with no hanger. The `eye_03`, `eye_33` and `eye_36` views all show a pale grey box.
- Code: lines 461-465.
- Fix: replace the steel body with a frame, keep the glass visible, and add a hanger:
  ```csharp
  d.AddBox(lanternAt + Vector3.up*0.2f,   new Vector3(0.3f,0.05f,0.3f), Mat.Steel);
  d.AddBox(lanternAt + Vector3.down*0.2f, new Vector3(0.3f,0.05f,0.3f), Mat.Steel);
  foreach (float qx in new[]{-0.13f,0.13f}) foreach (float qz in new[]{-0.13f,0.13f})
      d.AddBox(lanternAt + new Vector3(qx,0f,qz), new Vector3(0.03f,0.36f,0.03f), Mat.Steel);
  d.AddBox(lanternAt + Vector3.up*0.27f, new Vector3(0.02f,0.2f,0.02f), Mat.Steel); // hanger
  ```
  Make the glass box `(0.22f, 0.32f, 0.22f)`.

**16. Lamp and brazier rows are on different rhythms, so lamps pair up with braziers.**
- Lamps are at z = -22, -13, -4, 5, 14, 23 (spacing 9) and braziers at z = -12.15, -1.35, 9.45, 20.25 (spacing 10.8).
- That puts lamp 23 within 2.75 m of brazier 20.25, and lamps -13 and -12.15 nearly on top of each other. Lamps are at x ±4.6 and braziers at ±4.32, so the two rows are 0.28 m off line. See `plan_all`.
- Code: line 454 (`for (float z = -22f; z <= 24f; z += 9f, k++)`) and line 457 (`s * 4.6f`).
- Fix for the north half: in place of the loop, lay lamps at z = -22, -13, -4, 5, then 14.85 and 25.65, all at `s * 4.32f`. The 14.85 and 25.65 lamps sit 5.4 m from the neighbouring braziers, and lamp 14.85 and brazier 20.25 frame the tent mouth, whose centre is z 17.8.
- A lamp at z 25.65 does not hit the wagon, which is 3.4 m away.

**17. The hanging-lantern rope pole is 1 m from a lamp post.**
- Pole at (-5.54, 13.5), lamp at (-4.6, 14). Seen in `plan_NW` at disp (1535, 1122).
- Code: line 578, `foreach (float z in new[] { -6f, 2f, 10f })`.
- Fix: `{ -6f, 2f, 9f }`, which puts the third pole at z = 12.15. That is 2.7 m from both the brazier at 9.45 and the new lamp at 14.85.

**18. The rope segments don't reach the east pole.**
- Each segment is centred +0.51 from its node, but the node spacing is 1.384, so the last segment ends 0.18 m short of the east pole and the first overshoots the west one.
- Code: line 588, `p + new Vector3(0.51f, ...)`.
- Fix: `0.5f * 1.03f * Spread` (about 0.695).

## C. Carriage, gate and road

**19. The cobbled road ends 4.3 m before the gate.**
- It stops at z 29.7; the arch starts at z 34. The horse and Corwin stand on bare flagstone, and the road then jumps from 5.9 m to 4.4 m wide in the forest road. See `plan_all`, `eye_05_boarding_top` and `eye_08_carriage_top`.
- Code: lines 602-604.
- Fix: extend the road to z = 37, which is length 58.6 centred on z = 7.7:
  ```csharp
  d.AddBox(o + new Vector3(0f, 0.02f, 7.7f), new Vector3(5.9f, 0.04f, 58.6f), Mat.Rubble);
  d.AddBox(o + new Vector3(-2.97f, 0.06f, 7.7f), new Vector3(0.3f, 0.12f, 58.6f), Mat.StoneWall);
  d.AddBox(o + new Vector3( 2.97f, 0.06f, 7.7f), new Vector3(0.3f, 0.12f, 58.6f), Mat.StoneWall);
  ```

**20. The oak at the gate crowds the road and gate pillar, and covers the feed trough.**
- Oak at S(-2.8, 22.6) = (-3.78, 30.5), scale 0.85, with its trunk 0.8 m from the kerb.
- Its crown reaches z ≈ 32.7, about 1.3 m from the gate pillar torches, overhangs the road, and covers the trough at (-5.67, 30.38). It is the dark mass over the road in `eye_06_carriage_a`, `eye_03_boarding_a`, `eye_58_wall0_along` and `plan_NW`.
- Code: line 106, `(-2.8f, 22.6f, 0.85f)`.
- Fix: move it to `(-11.0f, 18.2f, 0.95f)` = (-14.85, 24.6).
  - This is 2.0 m from the tavern roof corner, 4.1 m from the tower, and 4.95 m from the relocated cart.

**21. The feed trough floats 0.15 m and is nowhere near the horse.**
- The box is centred at y 0.4 with height 0.5 (bottom at 0.15), at (-5.67, 30.38). The horse is at z ≈ 31.5, 5.7 m east.
- Code: line 572, `d.AddBox(o + S(-4.2f, 0.4f, 22.5f), new Vector3(0.8f, 0.5f, 2.2f), Mat.Wood);`
- Fix: `d.AddBox(o + S(-2.75f, 0.25f, 23.3f), new Vector3(0.8f, 0.5f, 2.2f), Mat.Wood);`, which is (-3.71, 0.25, 31.46). It sits by the horse and on the ground.

**22. Hay, barrels and crates on the east side overlap each other.**
- Barrel (5, 17.4) = (6.75, 23.49) is 0.81 m from hay (7.56, 23.49). The barrel's east edge is at 7.18 and the hay's west edge at 7.07, so they overlap.
- The crate cluster at S(7, 18) = (9.45, 24.3) piles against hay B (8.1, 25.38) and under the oak crown (`plan_NE`, `eye_44_omen_top`).
- A third bale at S(4.7, 21.6) = (6.35, 29.2) stands alone 5 m from the rest (`eye_05`).
- Code: line 497 (`(7f, 18f)` in the crate list), line 570-571 (barrels), line 561 (hay list).
- Fix, crates: delete the `(7f, 18f)` entry from the list at line 497.
- Fix, barrel: change line 571 to `Props.Barrel(d, o + S(4.7f, 0f, 17.2f));`.
- Fix, lone bale: change the third hay entry from `(4.7f, 21.6f, 30f)` to `(4.9f, 20.2f, 30f)`.

**23. `BoardingCenter` is orphaned on top of the wagon.**
- It is at S(0, 0, 18) = (0, 24.3), which is the wagon's tail step. The real boarding ground is the seat ground at z ≈ 23.45.
- `InBoardingCircle` is no longer used anywhere.
- Code: lines 28-29.
- Fix: `BoardingCenter = Origin + S(0f, 0f, 19.5f) + new Vector3(0f, 0f, -2.9f)`, with `BoardingRadius = 2.4f`.

**24. The forest-road lantern torches float 0.13 m under their arms.**
- The arm is at y 2.5 (bottom 2.46). The torch bracket is centred at 2.2 (top 2.33).
- Code: line 425, `post + new Vector3(-s * 0.55f, 2.2f, 0f)`.
- Fix: `post + new Vector3(-s * 0.55f, 2.34f, 0f)`.

## D. Planter row

**25. Planters are not mirrored.**
- West at (-6.21, 6.21), east at (6.21, 4.59), a 1.6 m offset. I did not check whether the east planter sits in front of the Runewright's ring.
- Code: line 121, `(4.6f, 3.4f)`.
- Fix: `(4.6f, 4.6f)`.

## Already right, leave alone
- Gatehouse pillars, arch, portcullis teeth, and the two gate torches (about 5 cm off the face).
- Curtain wall and towers, wall lanterns and banners clear of the buttresses, and the gate fully closed by the wall segments.
- Wagon centred on the road with wheel clearance, rear step and seat ground, Corwin seated on the box, and the horse.
- Both braziers' x position and 10.8 m rhythm (only the lamps move), and the kerbs.
- Tavern: the walls, the door, the sign arm and its lantern, the hearth, the bar and kegs, Marta behind her counter facing the customers, the band facing the room, and the tavern alley to the curtain wall (2.5 m, passable).
- Tent stations: slots against the back wall facing east, dice and coffer facing the mouth, Bones behind the blackjack table facing the players, and the rug and pole.
- Omen stone facing west toward the road, with the east oak 4.7 m behind it.
- Records slab orientation and gilding, and the bounty board orientation and roof pitch.

## Noticed in other zones
- The lamp at (4.6, -22) sits 0.5 m from a bonfire bench at 330 degrees, and lamp (-4.6, -6.75) would sit dead centre of Silas's counter if the grid were extended south. I left the south lamps unchanged.
- I did not audit the south or the east range.

### Zone: west

**West trade row audit: 12 placement problems, 2 minor ones, and one defect outside my zone**

I read Lobby.cs and the Props, Anvil and Wardrobe code, and checked each finding against renders. I did not edit any files.

Conventions: all coordinates are metres from the camp origin (x east, z north), and "S" means a Lobby.cs literal that gets the ×1.35 spread. Numbers in `S(...)` below are pre-spread.

## Problems

**1. Silas stands outside his own stall.**
- Where: Silas is at (-9.99, -6.75). The counter front is x = -6.48, the back posts are x = -8.88, and the awning ends at x = -9.18. He is 3.5 m behind the counter, past the back edge of the awning.
- His trestle table of wares is 0.9 m in front of him, at x ≈ -9.1. That is the awning's rear edge, so the stall counter in front is empty.
- Evidence: plan_SW (the figure and its lantern sit west of the red-striped stall), eye_09_merchant_a (a small table far behind the big counter), eye_10_merchant_b.
- Code: Lobby.cs:32, `MerchantSpot = Origin + S(-7.4f, 0f, -5f)`.
- Fix: `MerchantSpot = Origin + S(-6.2f, 0f, -5f)`. That puts him at (-8.37, -6.75) under the awning, with his table at x ≈ -7.47, about 0.25 m behind the counter's back face.
- He still faces east, so no other change is needed.

**2. Hobb stands inside the anvil's coal brazier and faces away from customers.**
- Where: Hobb is at `AnvilSpot + back*1.4` = (-7.56, 0.49). The anvil's own brazier is at about (-7.71, 0.84), 0.4 m from him, so he overlaps it.
- He is looking at `AnvilSpot + left*3`, which is west toward the forge. Customers come from the east and see his back.
- Evidence: eye_13_anvil_b (he overlaps the brazier), eye_12_anvil_a.
- Code: Lobby.cs:72 (`FolkBodies`) and Lobby.cs:241 (`Props.Townsfolk(... Props.Folk.Smith)`). The same expression is also used at CampChatter.cs:273, SmokeTest.cs:3877 and AutoPlay.cs:538.
- Fix: add `public static Vector3 HobbSpot => AnvilSpot + Vector3.left * 0.95f;`, which is (-8.51, 1.89), just west of the anvil.
  - Lobby.cs:72: `(FolkId.Hobb, HobbSpot)`.
  - Lobby.cs:241: `Props.Townsfolk(lights, HobbSpot, HobbSpot + Vector3.right * 3f, Props.Folk.Smith);`.
  - Replace `AnvilSpot + Vector3.back * 1.4f` with `HobbSpot` in CampChatter.cs:273 and SmokeTest.cs:3877.
  - AutoPlay.cs:538 has `AnvilSpot + Vector3.back * 1.6f`. Change it to `AnvilSpot + Vector3.right * 2.4f` so the bot stands in front of the counter, not inside the stall.

**3. The forge group is scattered and orphaned.**
- The forge is at (-11.07, -0.27). It is 2.2 m south of the anvil line, 2.2 m behind the stall, and half on the edge of the dark earth bed.
- The quench barrel is at (-9.18, 4.32). That is 2.9 m from the anvil, wedged against the stall's north-west post.
- The weapon rack is at (-11.61, 4.05). It stands alone in the open, 4 m from the stall, and is a leftover of the old layout.
- Evidence: plan_SW (the forge straddles the circle edge), plan_NW (the lone rack at lower centre), eye_14_anvil_top, eye_13_anvil_b.
- Code: Lobby.cs:647, 659 and 660.
- Fix: put the group on one axis behind the stall, aligned with the anvil.
  ```csharp
  var forge = AnvilSpot + new Vector3(-3.0f, 0f, 0f);                     // (-10.56, 1.89), 0.5 m clear of the awning
  ...
  Props.Barrel(d, AnvilSpot + new Vector3(-0.74f, 0f, 1.16f));            // quench barrel under the awning, beside Hobb
  Props.WeaponRack(d, AnvilSpot + new Vector3(-3.0f, 0f, 2.9f), Quaternion.Euler(0f, 90f, 0f)); // in line with the forge, facing east
  ```
- The forge glow light and `ForgeChimney` follow the `forge` variable, so they need no edit.

**4. A banner pole pierces the merchant's awning, and both banners face the wall.**
- Where: the west pole is at (-8.37, -8.1). That is inside the stall footprint, 0.7 m from the back-south post, and a 6 m pole goes through the awning at y ≈ 3.
- `Props.Banner` puts the emblem on the face given as `outward`. The code passes `Vector3.right * s`, which points away from the road, so the skull emblem faces the curtain wall.
- Evidence: eye_10_merchant_b (the pole and banner come out of the awning, emblem visible from the west).
- Code: Lobby.cs:531-537.
- Fix: move the poles into the gap between the two west stalls and flip the facing:
  ```csharp
  var pole = o + S(s * 6.2f, 0f, -1.8f);   // z = -2.43, level with the planters
  ...
  Props.Banner(lights, pole + new Vector3(0f, 5.7f, 0.65f), Vector3.left * s, ...);
  ```
- I checked the east side: (8.37, -2.43) is clear. Do not use z = -8.2 there, because the east oak crown would swallow the pole.

**5. The rope-lantern poles stand in front of the counters.**
- Where: the poles are at x = ±5.535, z = -8.1, 2.7 and 13.5, because `S(s*4.1, 2.3, z)` also scales z. Poles at -8.1 and 2.7 sit 0.47 m in front of the merchant and smith counters, and the third (13.5) is 0.9 m from the lamp post at z = 14.
- Evidence: eye_12_anvil_a (a tall pole right at the counter end), eye_09_merchant_a.
- Code: Lobby.cs:578-596. The `foreach (float z in new[] { -6f, 2f, 10f })` loop uses `S(...)` for the pole and for the rope points.
- Fix: use literal world z values that avoid the stalls, lamps, braziers and planters, and keep the x scaling:
  - `foreach (float z in new[] { -10f, -1f, 8f })`.
  - Pole: `o + new Vector3(s * 4.1f * Spread, 2.3f, z)`.
  - Rope point: `o + new Vector3((-4.1f + u * 8.2f) * Spread, 4.6f - sag, z)`.
  - `at` for the torch: `o + new Vector3(0f, 4.2f, z)`.
- The nearest neighbour to any pole is then 1.3 m or more.

**6. The lamp and brazier rows collide at z = -13.**
- Where: lamps are every 9 m at z = -22, -13, -4, 5, 14, 23, but the braziers are every 10.8 m at -12.15, -1.35, 9.45, 20.25. At z = -13 the lamp (x = -4.6) and brazier (x = -4.32) are 0.85 m apart, on both sides of the road. The spacing is uneven everywhere else too.
- Evidence: plan_SW (lamp glow with a brazier bar directly under it at lower right), plan_all (both sides).
- Code: Lobby.cs:605-609 (`for (float z = -9f; z <= 15f; z += 8f)` with `S(±3.2f, 0f, z)`).
- Fix: put the braziers midway between the lamps.
  ```csharp
  for (float z = -8.5f; z <= 18.5f; z += 9f)
  {
      Props.Brazier(lights, o + new Vector3(-4.32f, 0f, z), Fire, false);
      Props.Brazier(lights, o + new Vector3(4.32f, 0f, z), Fire, z > 15f);
  }
  ```

**7. The training yard is incoherent.**
- The straw is Mat.Wood boards, 5 x 9 m, at (-17.55, -18.2). It reads as a giant table (eye_55_dummies_b, eye_67_wall225_in).
- The fence is at x = -13.77, 1.3 m outside the deck edge (-15.05).
  - Its posts run z = -23.6 to -12.8 every 2.7 m, but the rail only runs -22.2 to -14.2. The two end posts are lone sticks (eye_54, eye_55).
- Brann is at (-12.29, -11.07). That is 2.6 m north of the deck's top edge and past the fence's north end, not in the middle of the yard.
- The weapon rack at (-21.33, -18.2) stands 1.3 m west of the deck with nothing around it.
- The brazier at (-14.85, -24.98) is not in line with the fence end.
- Brann's "rack of practice swords" is four loose sticks leaning on nothing.
- Evidence: plan_SW, eye_27_yard_a, eye_28_yard_b, eye_29_yard_top, eye_54, eye_55, eye_67_wall225_in.
- Code: Lobby.cs:59 (`YardSpot`) and Lobby.cs:885-901 (`BuildYard`).
- Fix:
  ```csharp
  public static readonly Vector3 YardSpot = Origin + new Vector3(-12.9f, 0f, -18.2f);  // level with the deck centre
  ...
  var deck = o + S(-13f, 0f, -13.5f);                                                  // (-17.55, -18.2)
  d.AddBox(deck + Vector3.up * 0.02f, new Vector3(5f, 0.03f, 9f), Mat.Cloth, new Color(0.5f, 0.42f, 0.2f)); // straw, not planks
  float fx = deck.x + 2.95f;                                                           // -14.6, hugging the east edge
  for (int i = 0; i <= 6; i++) d.AddBox(new Vector3(fx, 0.5f, deck.z - 4.5f + i * 1.5f), new Vector3(0.14f, 1f, 0.14f), Mat.Wood);
  d.AddBox(new Vector3(fx, 0.85f, deck.z), new Vector3(0.08f, 0.1f, 9f), Mat.Wood);
  Props.WeaponRack(d, new Vector3(o.x - 20.6f, o.y, deck.z), Quaternion.Euler(0f, 90f, 0f));
  Props.Brazier(lights, new Vector3(fx, o.y, deck.z - 5.9f), Fire, false);
  ```
  - Add a 1.3 m wooden crossbar behind Brann's practice swords at the same height, so they hang from something.
- The dummies at `DummySpots` (z = -21.6 and -14.85) are already symmetric about z = -18.2. Leave them.

**8. The tailor's cloth is floating.**
- Cloth bolts: they sit at `TailorSpot + facing*1.5` → x = -18.21, y = 1.02. The counter spans x -17.79 to -16.84 with its top at y = 1.05. So the bolts hover 0.3 m behind the counter's back edge, half below its top height.
- Hanging-cloth rail: the rail and `HungCloth` strips are at x = -20.61, 0.9 m behind the back posts (x = -19.71). It is a 2.8 m rail at y = 2 with no supports.
- The strips are off-centre in z (-1.1 to +0.9 around the stall centre).
- Evidence: eye_46_tailor_b, eye_47_tailor_top, eye_45_tailor_a.
- Code: Lobby.cs:692-699.
- Fix:
  - Bolts: `TailorSpot + TailorFacing * 2.4f + new Vector3(0f, 1.17f, -1.0f + i * 0.5f)`.
  - Rail: `TailorSpot - TailorFacing * 0.1f + new Vector3(0f, 2f, 0f)` with size (0.06, 0.06, 3.6), so it spans the two back posts.
  - Strips: `TailorSpot - TailorFacing * 0.2f + new Vector3(0f, 1.32f, -1.0f + i * 0.5f)`.

**9. A crate and barrel cluster crowds the mannequin and the tailor's stall corner.**
- Where: the cluster at `S(-12,-2)` = (-16.2, -2.7) ±0.8. A barrel is about 0.5 m from the mannequin plinth (-15.39, -1.2), and the cluster touches the tailor awning's north-east corner (-16.8, -3.5).
- Meanwhile there is a large empty void further west.
- Evidence: eye_48_mannequin_a, eye_49_mannequin_b, eye_47_tailor_top, plan_SW.
- Code: Lobby.cs:497, first tuple `(-12f, -2f)`.
- Fix: `(-21f, -2f)`, which is (-28.35, -2.7), 3.7 m inside the west tower. That uses the void instead of crowding the plinth.

**10. The dark earth bed is off-centre and the forge sits on its rim.**
- Where: the bed is at (-15, 0) with radius 4.5. The mannequin is 1.3 m south of its centre, and the forge straddles its east edge.
- Evidence: plan_SW, eye_50_mannequin_top.
- Code: Lobby.cs:275, `new Vector3(-15f, 0f, 0f)`.
- Fix: `new Vector3(-15.4f, 0f, -1.2f)` with radius 3.4, centred on the mannequin. That leaves 0.5 m clear of the new forge position. If you want it to remain a loop, change that array entry to a tuple.

**11. Two oaks are too close to neighbours.**
- Oak (-12.6, 6.6) is at (-17.0, 8.9) with scale 1.25. Its crown's north edge is about 1.2 m from the tavern eave (z ≈ 12.2).
- Oak (-6.8, -11.2) is at (-9.18, -15.12). Its crown is about 0.7 m from the bonfire planter (-6.24, -15.3), and it would crowd Brann's new spot.
- Evidence: plan_NW (oak under the tavern), plan_SW (oak beside the planter), eye_71_wall315_in.
- Code: Lobby.cs:106.
- Fix: replace `(-12.6f, 6.6f, 1.25f)` with `(-13f, 4.4f, 1.25f)`, and `(-6.8f, -11.2f, 0.95f)` with `(-8.15f, -11.2f, 0.95f)`.

**12. Banner poles and lamp row lamps are fine, but the camp's grass discs read as black holes.**
- Where: the 4 grass patches at lines 518-522 use tint (0.13, 0.19, 0.08), which renders almost black at night. See eye_28_yard_b and eye_46_tailor_b (a black ellipse by the yard), and plan_SW (a flat dark disc by the yard).
- Fix: lighten to `new Color(0.2f, 0.3f, 0.12f)`. Low priority, cosmetic.

## Minor

- The mannequin is 5.6 m from Mistress Vey. It works as a vignette on its own bed, so leave it unless you want it beside her stall.
- The anvil at 0.9 m is just below the counter top (1.05 m), but it is visible over the counter from 2 m away.

## Seen but not in my zone

Lobby.cs:507, the hand cart, `S(-7,14)` = (-9.45, 18.9), is inside the gambling tent's radius (the tent is at (-11.88, 17.8), radius 4.6). The cart is hidden under the red roof in plan_all and plan_NW.

## Already right, leave alone

- Both stalls share the counter line x = -6.48, with matching awnings and colours.
- Vey stands behind her counter, centred on z, facing east.
- The merchant faces east with his table between him and the party.
- The anvil faces east.
- The west planters at z = -2.43 and 6.21 flank Hobb's stall symmetrically.
- The `ForgeChimney` smoke point is at the top of the stack, which is attached to the forge.
- The tailor stall is fine in position and colours.
- The two dummies are symmetric on the deck.

The main file is C:/Users/User/Desktop/game_prototype/Deepbound/Assets/Scripts/World/Lobby.cs.

### Zone: east

**EAST ZONE AUDIT (read-only, nothing edited)**

How I read the renders: in plan_NE/SE, x = 17.1 + (px−1024)/46.5 and z = ±17.1 ∓ (py−1024)/46.5, using original-resolution pixels. I checked every object below against these renders and against the code. All coordinates are metres from origin. `Spread` = 1.35, and `Origin` is dropped from the numbers.

---

## A. Clear defects (fix all)

**A1. Hay bales: floating, wrong material, one clips the wall**
- Where: the five "bales" at about (28.6, 10.1–18.8), against the east wall.
- Evidence: plan_NE (stack at displayed x≈1470, y 865–1300), eye_51_range_a (dark boxes in the air behind targets 1 and 2), eye_53_range_top, eye_62_wall90_along.
- Code: Lobby.cs:999-1000
  `d.AddBox(Origin + S(21.2f, 0.45f + (i % 2) * 0.9f, 7.5f + i * 1.6f), new Vector3(1f, 0.9f, 1.5f), Mat.Wood, Quaternion.Euler(0f, 12f * (i % 3), 0f));`
- Problems:
  - Bales 1 and 3 sit at y=1.35 (bottom at 0.9) over empty ground. Their z is 2.16 m apart, so nothing is under them.
  - They are `Mat.Wood`, so they read as crates. The carriage hay uses `Mat.Cloth` tinted (0.62, 0.5, 0.22).
  - Bale 4 is at (28.62, 18.77), r=34.3, with a 12° yaw. Its outer corner reaches r≈35.2, but the wall's inner face is at r=35.0, so it pokes into the wall.
  - The row runs on a straight x line but ends up touching the wall.
- Fix: replace lines 999-1000 with:
```csharp
var hay = new Color(0.62f, 0.5f, 0.22f);
for (int i = 0; i < 5; i++)
{
    var bp = Origin + new Vector3(28.2f, 0.45f, 10.3f + i * 1.9f);
    d.AddBox(bp, new Vector3(1f, 0.9f, 1.6f), Mat.Cloth, hay, Quaternion.Euler(0f, 6f * ((i % 3) - 1), 0f));
    if (i == 1 || i == 3) d.AddBox(bp + Vector3.up * 0.9f, new Vector3(1f, 0.9f, 1.6f), Mat.Cloth, hay, Quaternion.Euler(0f, -5f, 0f)); // stacked on a real bale
}
```
  The last bale then sits at r≈33.4 (reach 34.3, 0.7 m clear). The rail end of target 3 is at x=26.83, so there is 0.9 m clear.

**A2. Lamp post standing inside a bench, plus a lamp stacked on a brazier, plus uneven rhythm**
- Where: east lamp at (4.6, −22), the bench at (4.5, −21.5), the lamp at (4.6, −13) and the brazier at (4.32, −12.15).
- Evidence: plan_SE at displayed (405, 1180) (white lamp dash overlapping a brown bench plank) and at (405, 748–785) (lamp dash directly under a brazier disc). The west side mirrors it.
- Code:
  - Lobby.cs:454 `for (float z = -22f; z <= 24f; z += 9f, k++)`
  - Lobby.cs:457 `var foot = o + new Vector3(s * 4.6f, 0f, z);`
  - Bench ring: Lobby.cs:484 (`fire + dir*5.2f`, i=5 gives (4.5, −21.5)).
  - Braziers: Lobby.cs:605-608 (x=±4.32, z = −12.15, −1.35, 9.45, 20.25, spacing 10.8 m).
- Problems:
  - The lamp post at (4.6, −22) is 0.5 m from the bench centre, so it stands inside the bench.
  - The lamps use x=4.6 (not scaled) at a 9 m spacing. The braziers use x=4.32 at a 10.8 m spacing. So the two rows are almost on one line with different rhythms. The pairs end up 0.85 m, 2.65 m, 2.75 m, 4.45 m and 4.55 m apart.
- Fix: put the lamps midway between braziers, on the brazier line:
  - Line 454: `for (float z = -6.75f; z <= 25.7f; z += 10.8f, k++)` (lamps at −6.75, 4.05, 14.85, 25.65).
  - Line 457: `var foot = o + new Vector3(s * 4.32f, 0f, z);`
  - This removes the bench clash at z=−22 and the brazier near-overlap at z=−13.
  - Check that the lamp at (4.32, 25.65) clears the barrel at S(4.4,18.2) = (5.94, 24.57). It is 1.9 m away, which is fine.

**A3. Crate and barrel group in the archery firing lane**
- Where: (16.9 ± 0.8, 12.2 ± 0.8), 3.4 m in front of the shooting line.
- Evidence: eye_51_range_a (huge crate and barrel in the foreground, blocking the south end of the lane), eye_59_wall45_in, eye_53_range_top (bottom, on the earth bed).
- Code: Lobby.cs:497 list entry `(12.5f, 9f)`.
- Problem: the shooters stand at x=13.5, z 11.85–17.85 and fire east. The south end of the targets' travel (z≈11) passes through this group. The group is also random-scattered (±0.8), so its crates touch each other.
- Fix: change `(12.5f, 9f)` to `(8f, 11.5f)`. That is (10.8, 15.5), behind the shooting line, so it reads as an ammo store. It is 3.1 m from the brazier at (12.7, 18.45). The RNG call count is unchanged, so the other groups do not shift.

**A4. Orphaned crates and barrel left from the old yard position**
- Where: crates at (18.9, −18.9) and (20.52, −17.28), barrel at (18.63, −16.47), in the SE.
- Evidence: plan_SE at displayed (1033–1115, 937–1043), eye_63_wall135_in (three props sitting loose in open floor), eye_24_shrine_a (right edge).
- Code: Lobby.cs:898-900 inside `BuildYard`. This is the yard that moved west to YardSpot, and these props are leftovers. They also sit near a second cluster, the plaza group `(11f, -11f)` at (14.85, −14.85).
- Fix: delete lines 898-900. Two clusters 5 m apart in the same SE spot is clutter.

**A5. Target rails are off-centre under the target sledges**
- Where: all three targets.
- Evidence: eye_51_range_a (the base sits on the rear rail, not between the pair), eye_59_wall45_in.
- Code: Lobby.cs:989-990
  `AddBox(t + (0,0.03,0), (0.12,0.06,7.6))` and `AddBox(t + (0.5,0.03,0), ...)`
  The sledge (RangeTarget.cs:83) is 0.6 wide in x, centred on t.x, so it spans ±0.3. The rails are at t.x and t.x+0.5, so the pair is centred 0.25 m east of the target.
- Fix: use `t + new Vector3(-0.25f, 0.03f, 0f)` and `t + new Vector3(0.25f, 0.03f, 0f)`.
  The sledge's travel is ±3.2 plus a 0.5 half-length, which is inside the rail's ±3.8, so the rail length is fine.

---

## B. Placement and orientation problems

**B1. Ysolde's rune table is behind her back, and the ring's opening does not face the road**
- Where: table at (17.91, 4.57), Ysolde at (17.01, 4.32), stones around centre (17.81, 4.52).
- Evidence: eye_30_runewright_a (she faces the camera and the table is behind her), eye_31_runewright_b (table is between us and her back), eye_32_runewright_top.
- Code:
  - Lobby.cs:944-946 (table x offset +0.9), 949 (tiles at +0.85…), 952 (candles at +0.95).
  - PlayerProgress.cs:663 (embers at +0.9).
  - Lobby.cs:917 `float a = (35f + i * 58f) * Mathf.Deg2Rad;`
- Problems:
  - She faces west (Lobby.cs:245) toward customers, but her counter stands east of her. A customer meets her face first and the table is unused behind her.
  - The comment says "open side facing the road". The 70° gap is at 325°–35°, which is the east side. The west side has the same 3 m gaps as every other pair.
- Fix:
  - Move the table to the customer side. Replace every `0.9f`, `0.85f` and `0.95f` x offset in lines 944-952 with `-0.9f`, `-0.95f` (plus the `(i%2)*0.15f` term) and `-0.85f`. Change PlayerProgress.cs:663 to `new Vector3(-0.9f, 1f, 0.25f)`.
  - Turn the ring so the wide gap faces west: line 917 becomes `(215f + i * 58f)`. The mouth is then at x≈15.3, z 2.7–6.3, with the table in it and her behind it.
  - The lintel (line 932) then lands on the SE pair, which is fine.

**B2. Shrine front step reads as a thin black stripe**
- Where: along the shrine's west edge, at (19.2, −5.4).
- Evidence: eye_24_shrine_a (black bars along the front edge), eye_26_shrine_top.
- Code: Lobby.cs:975 `d.AddBox(s + new Vector3(-1.4f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 3.2f), Mat.StoneWall);`
  The platform (line 974) spans x −1.5…+1.9 and is 0.12 tall. The step spans −1.7…−1.1 and is 0.06 tall, so it is hidden under the platform and only a 0.2 m by 0.06 sliver shows.
- Fix: change it to `new Vector3(-1.8f, 0.03f, 0f)`. It then spans −2.1…−1.5 and is a real visible step.

**B3. Brother Aldric kneels with his back to the altar and blocks it**
- Where: (19.73, −5.4), 0.8 m in front of the altar front face.
- Evidence: eye_24_shrine_a (he hides the altar's right front), eye_26_shrine_top.
- Code:
  - Lobby.cs:242 `Props.Townsfolk(lights, ShrineSpot + Vector3.left * 1.2f, ShrineSpot + Vector3.left * 4f, Props.Folk.Priest)`. His look target is 4 m west, so he faces the plaza with his hands out in front and the altar behind him.
  - Matching copies: Lobby.cs:73, CampChatter.cs:292, SmokeTest.cs:3717.
- Fix, so a praying priest faces the altar:
  - Change the position to `ShrineSpot + new Vector3(-1.2f, 0f, 1.1f)` and the look target to `ShrineSpot`.
  - Apply the same position in the three matching copies.
  - He then kneels beside the caskets, not in front of them.

**B4. Gong: the disc is hung 0.3 m off the crossbeam, and the banner is half hidden behind the post**
- Where: disc hangs at x=11.475. The frame (posts and beam) is at x=11.775, and the banner is at (11.925, 9.6).
- Evidence: eye_18_gong_a (banner peeks out from behind the left post and the disc), eye_19_gong_b.
- Code:
  - Lobby.cs:705 `g + new Vector3(0.3f, 1.8f, z)` and 706 `g + new Vector3(0.3f, 3.65f, 0f)`. The disc pivot is at x=0 (Gong.cs:99), so the cord is 0.15 m in front of the beam's face and hangs from air.
  - Lobby.cs:708 `Props.Banner(lights, g + new Vector3(0.45f, 3.2f, 1.5f), Vector3.left, ...)`. The banner is on the post's back face, behind the gong from the customer side. It sits 0.35 m below the beam, with the rod unattached.
- Fix:
  - Posts and beam: change `0.3f` to `0f` in lines 705 and 706.
  - Banner: hang it outside the disc's swing from the beam's customer face. Use `g + new Vector3(-0.05f, 3.45f, 2.3f)` with `Vector3.left`, and widen the beam in line 706 to `new Vector3(0.3f, 0.3f, 6f)`.
  - If you want a single banner, mirror it to z=−2.3 for balance.

**B5. Earth beds are off-centre and not scaled**
- Where: bed at (14, 14) r 4.5 and bed at (15, −12) r 4.5.
- Evidence: plan_NE (the shooting-line rail at x=13.5 cuts through the bed off-centre, and the gong beam's north end touches the bed's south edge), plan_SE (yew 2.3 m from centre).
- Code: Lobby.cs:275 `new Vector3(14f, 0f, 14f)` and `new Vector3(15f, 0f, -12f)`. These do not use `S()`, unlike every other camp position.
- Fix:
  - Bed 1: `new Vector3(13.5f, 0f, 14.85f)`, centred on the shooting line. Its south edge is then z=10.35, so the gong frame (z up to 9.9) clears it.
  - Bed 2: `new Vector3(17.3f, 0f, -11.6f)`, centred on the yew.
  - Check that the plaza crate group at (14.85, −14.85) is 4.1 m from the new centre, still inside r=4.5.

**B6. East planters are uneven and not on the road rhythm**
- Where: (6.21, −2.43) and (6.21, 4.59), against the west pair at z −2.43 and 6.21.
- Evidence: plan_NE (displayed (488, 1513) and (488, 1822)).
- Code: Lobby.cs:121 `(4.6f, -1.8f)` and `(4.6f, 3.4f)` for the east side, `(-4.6f, -1.8f)` and `(-4.6f, 4.6f)` for the west.
- Problems:
  - The east pair's second planter is 1.6 m off the west one.
  - It sits 0.4 m beside the old lamp at z=5.
  - The first planter is 0.6 m clear of the brazier bowl at (4.32, −1.35).
- Fix: after the A2 change, use `(4.6f, -3.0f)` and `(4.6f, 5.0f)` for the east planters, which gives world z −4.05 and 6.75. These are midway between the new lamp and brazier. Optionally mirror them on the west pair.

**B7. Gong and range crowd each other while the east plaza stays empty**
- Where: gong frame north end z=9.9, shooting-line south sign post at z=11.45, a gap of 1.6 m. Meanwhile x 6–10, z 10–19 and x 6–13, z −12 to −3 are empty.
- Evidence: plan_all, eye_20_gong_top (beam running into the range's sign post and rail).
- Fix, optional but worth doing: shift the whole range north by 1.5 m. That means `TargetSpots` (Lobby.cs:170-172: z 11 → 12.1, 11.5 → 12.6, 10.5 → 11.6), `RangeCenter` (line 174, 11 → 12.1) and `line` (line 993, z 11 → 12.1). Check that the target 3 rail end (z≈19.6) and the hay at x=28.2 stay inside r<34, and shift the earth bed by the same amount.

---

## C. Minor

- **C1. Dead tree lines up with the stones from the east.** The dead tree at (25.65, 1.35) (Lobby.cs:523) reads as growing out of one menhir in eye_61_wall90_in and eye_30_runewright_a. Nudge it to `S(19f, 0f, -2f)`, which is (25.65, −2.7), and move the grass patch (line 518) the same way.
- **C2. Menhir lean.** The lean in line 921 (±3–4° pitch plus roll) reads as about 8° because the stones are flattened to 0.62. Use `i % 2 == 0 ? 2f : -2f` for pitch and ±1.5° for roll. The lintel (line 932) is on stones i1 and i2 (k 0.99 and 1.13), not the tallest pair, so it sits tilted about 7°. This is acceptable as intentional.
- **C3. Crate group S(7,18)** at (9.45, 24.3) sits 2.9 m from the NE oak trunk at (11.07, 21.87), tucked under the crown (plan_NE, displayed (630, 650)). Move it to `(6f, 20f)` if you want clearance. It is outside my zone.
- **C4. Shrine backing wall.** Its east face is a blank 4×3.6 m slab seen from the wall side (eye_25_shrine_b, eye_61_wall90_in). It is not a defect, but a banner or torch on the rear face would finish it.

---

## D. Already right (leave alone)

- **Gong:**
  - The disc faces west toward the customer approach, `GongFacing` = left.
  - The disc hang height (disc centre 1.9 m, top 3.05 m) fits under the beam.
  - The rubble pad is placed 1.4 m in front as a stand spot.
  - It is clear of the planter at (6.21, 4.59), 2.3 m away.
- **Runewright:**
  - Glow rings and rune marks are correct (ring radius 2.35–2.55, marks at 2.1).
  - Stones sit flat on the ground.
  - Key light and `RuneLight` positions are good.
  - The ring is clear of the shrine, range and tree (≥3.4 m to the nearest rail end).
- **Shrine:**
  - The altar, plinth, saint and halo are correctly spaced from the backing wall (0.6 m gap).
  - The braziers flank the platform symmetrically (z ±1.9).
  - The banners hang on the wall face with the halo clear of them (inner edges ±0.6).
  - The backing wall is 11 m from the curtain wall.
- **Range:**
  - Targets face west toward the shooters, and the 3.2 m travel fits the 3.8 m rails.
  - The shooting line, posts and sign are fine. The sign board faces ±x (readable from both sides).
  - The north-end brazier is fine.
- **Trees:**
  - The yew at (17.28, −11.61) reads as a yew. The raised skirt with a bare trunk is intended.
  - The NE oak at (11.07, 21.87) clears the omen stone and the road lamps by more than 4 m.
  - The SE oak at (8.64, −12.96) is fine.
- **Road and walls:**
  - The grass patches are fine. The SE patch (24.3, −20.25) r 2.8 stays 0.6 m from the wall.
  - The wall torches and banners at 22.5° and −22.5° are well placed.
  - The braziers on the road line (x=±4.32) are evenly spaced at 10.8 m.

### Zone: centre-south

# Audit of the centre and south of the camp

I checked each finding against the code and a render. The code has one more problem than the renders do: a lot of this zone's layout still uses the old unscaled positions. All coordinates are metres east and north of the origin (x, z). `Lobby.cs` S-units are multiplied by 1.35. The PNGs are in `Builds/DeepboundNext/tour/`.

## Problems with the tour renders

- The "bonfire" station is `("bonfire", o)` in `CampTour.cs` (~line 79). That is the camp centre, not the fire at (0, -18.9). Use `o + Lobby.S(0,0,-14)`.
- The wall180 station sits at (0, -31.2), inside the keep's door slab. That is why `eye_65_wall180_in.png` is black and `eye_66` shows a plank. These two images prove nothing, so I judged the south wall from `plan_SW.png` and `plan_SE.png`.
- The clouds are `Mat.Smoke` puffs at y=46. The plan camera sits above them, so they show as the big translucent dark polygons outside the wall and in the east and south-east of `plan_all.png`. They are sky, not camp, and belong. Hide them in the plan renders with `foreach (var c in FindObjectsByType<Art.NightSky>(FindObjectsSortMode.None)) foreach (var t in c.Clouds) t.gameObject.SetActive(false);`.

## The dark discs on `plan_all.png`

There are three kinds besides the clouds.

1. **Three pebble-textured discs, 9 m across** (`Lobby.cs:274-276`, `BuildGround`). These are "earth beds": `Mat.Rubble` cylinders, r=4.5, 6 cm high. They use unscaled coordinates `o + c`, so they never moved with the 1.35 spread.
   - (-15, 0) sits under the mannequin turntable and the forge.
   - (15, -12) sits under the yew and a crate pile.
   - (14, 14) sits under the archery shooting line.
   - They belong to nothing.
2. **Four flat dark grass patches** (`Lobby.cs:518-522`), at (-24.3, -21.6), (24.3, -20.25), (-25.65, 5.4) and (25.65, 1.35), r=2.5-3.2.
   - Only the last one has a purpose (the dead tree).
   - The (24.3, -20.25) one looks black because the tower and wall shadow falls on it in the plan render.
3. **The black octagons** are tower cones and the keep spires. They belong.

**Fix.**
- Delete lines 274-276 (the `foreach` over the three beds).
- Keep only the `(19f, 1f, 3f)` entry in the grass array and delete the other three.

## Fixes, most important first

### 1. Well, planters, bench and bonfire sit on the gravel road
The road runs to z=-21.6 (`BuildRoad`, lines 602-604), so the bonfire is on gravel.
- Bonfire logs: the four radial logs reach x=3.3, across the kerb at 2.97 (`plan_SW`: log tips cross the kerb).
- Planters: the two at (±2.43, -14.58) straddle both kerbs (`plan_SE` at 110,855 and 322,855; `eye_40_well_b.png`).
- Bench: the 90° bench sits at (0, -13.7), in the road, 1 m from the well roof.
- Well: it stands on the road axis at (0, -11.34) (`eye_39_well_a`, `eye_41_well_top`).

**Fix 1a. Start the road north of the well.** Replace lines 602-604:
```csharp
const float z0 = -8.4f, z1 = 29.7f; float mid = (z0 + z1) / 2f, len = z1 - z0;
d.AddBox(o + new Vector3(0f, 0.02f, mid), new Vector3(5.9f, 0.04f, len), Mat.Rubble);
d.AddBox(o + new Vector3(-2.97f, 0.06f, mid), new Vector3(0.3f, 0.12f, len), Mat.StoneWall);
d.AddBox(o + new Vector3(2.97f, 0.06f, mid), new Vector3(0.3f, 0.12f, len), Mat.StoneWall);
```

**Fix 1b. Drop the bench in the road.** In the bench loop (line 480), add `if (i == 1) continue;` as the first statement. The ring then opens toward the road. The planters (`i % 2 == 0`) are handled in fix 1c.

**Fix 1c. Rebuild the planters.** Replace the `if (i % 2 == 0) {...}` block (lines 488-494) with nothing. After the bench loop add:
```csharp
foreach (var pl in new[] { fire + new Vector3(6.24f, 0f, 3.6f), fire + new Vector3(-6.24f, 0f, 3.6f), o + new Vector3(-3.2f, 0f, -30.1f), o + new Vector3(3.2f, 0f, -30.1f) })
{
    d.Add(MeshKit.Cylinder(0.55f, 0.6f, 0.6f, 10), Mat.StoneWall, Matrix4x4.Translate(pl));
    var soil = MeshKit.Part(lights, "PlanterShrub", MeshKit.Ball(0.55f, 8, 6), Mat.Cloth, pl + Vector3.up * 0.75f, default, new Vector3(1f, 0.75f, 1f));
    ArtLibrary.Tint(soil.GetComponent<Renderer>(), new Color(0.12f, 0.2f, 0.09f));
}
```
- The first two entries are the old 30° and 150° planters.
- The last two flank the keep doors. They replace the planter that stood on the door axis at (0, -26.1).
- On the door axis a planter and a bench stood in front of the doors.

**Fix 1d. Remove the two flower planters in the road.** At line 121 delete `(-1.8f, -10.8f), (1.8f, -10.8f)`. While there, change `(4.6f, 3.4f)` to `(4.6f, 4.6f)` so the rows match. The west pair is at z=-2.43 and 6.21, but the east one was at 4.59.

### 2. Lamp posts, braziers, hanging poles and banner poles are one jumble
Spacings differ: lamps every 9 m, braziers every 10.8 m, hanging poles at -8.1, 2.7 and 13.5. The gaps between a lamp and its nearest brazier run 0.85 to 4.55 m.
- A lamp stands 0.3 m from a brazier at z -13 and -12.15 (`eye_39_well_a`, `plan_SW` 1527,747-786).
- The lamp at (-4.6, -22) touches the bench at (-4.5, -21.5).
- The lamp at (-4.6, 23) stands in front of the bounty board centre.
- The brazier at (-4.32, 20.25) is 1 m from the Records slab (`plan_all` 833,490 next to 863,488). It also sits in front of the omen stone.
- The hanging-lantern poles at x=±5.535 stand 0.47 m in front of the smith counter (z 2.7) and the merchant counter (z -8.1).
- Banner poles at S(±6.2,-6) = (±8.37, -8.1) sit 2.8 m from those poles. The west one is inside the merchant stall's footprint (x -9.18..-5.98, z -8.67..-4.83), so it pierces the awning.
- Both banners face away from the road, so the emblem is not visible from it (`eye_40_well_b.png` shows the back of a plain red banner).

**Fix 2a. Delete the lamp-post loop in `BuildPlaza`.** Remove lines 451-477, and the `int k = 0;` on line 453.

**Fix 2b. Braziers.** In `BuildRoad`, change the loop and the shadow flag:
```csharp
for (float z = -9f; z <= 7f; z += 8f) { Props.Brazier(lights, o + S(-3.2f, 0f, z), Fire, false); Props.Brazier(lights, o + S(3.2f, 0f, z), Fire, z > 5f); }
```
This drops the pair at z=20.25. The result is braziers at -12.15, -1.35 and 9.45.

**Fix 2c. Rewrite the lantern strings.** Replace lines 577-596 of `BuildCastleLife` (poles are now just outside the kerb, midway between braziers):
```csharp
foreach (float z in new[] { -6.75f, 4.05f, 14.85f })
{
    foreach (float s in new[] { -1f, 1f }) d.AddBox(o + new Vector3(s * 3.5f, 2.3f, z), new Vector3(0.14f, 4.6f, 0.14f), Mat.Wood);
    for (int k = 0; k <= 8; k++)
    {
        float u = k / 8f;
        var p = o + new Vector3(-3.5f + u * 7f, 4.6f - Mathf.Sin(u * Mathf.PI) * 0.45f, z);
        if (k < 8)
        {
            float u2 = (k + 1) / 8f;
            var q = o + new Vector3(-3.5f + u2 * 7f, 4.6f - Mathf.Sin(u2 * Mathf.PI) * 0.45f, z);
            d.AddBox((p + q) / 2f, new Vector3((q - p).magnitude, 0.025f, 0.025f), Mat.Leather, Quaternion.FromToRotation(Vector3.right, q - p));
        }
        if (k > 0 && k < 8)
        {
            d.AddBox(p + Vector3.down * 0.12f, new Vector3(0.012f, 0.22f, 0.012f), Mat.Steel);
            d.AddBox(p + Vector3.down * 0.3f, new Vector3(0.12f, 0.16f, 0.12f), Mat.Flame);
        }
    }
    var sl = new GameObject("StringLight").AddComponent<Light>();
    sl.transform.SetParent(lights, false);
    sl.transform.position = o + new Vector3(0f, 3.85f, z);
    sl.type = LightType.Point; sl.color = new Color(1f, 0.75f, 0.4f); sl.range = 9f; sl.intensity = 1.6f; sl.shadows = LightShadows.None;
}
```
This also fixes three smaller faults in the old code.
- **Floating torch.** `Props.WallTorch(lights, at, ...)` at (0, 4.2, z) hangs in mid-air at the centre of each string with nothing holding it. `eye_00_bonfire_a.png` shows it with a raven perched on it; `eye_40_well_b.png` shows it above the road.
- **Rope offset.** The rope segment offset `0.51f` is a leftover from before the spread. It should be 0.692 (half of 8.2×1.35/8). As written the west end overshoots its pole by 0.18 m and the east end stops 0.18 m short.
- **Light count.** Net lights drop by about 3, since six lamp lights go and three torch lights become three plain ones.

**Fix 2d. Banner poles** (lines 531-537). Replace the whole block:
```csharp
foreach (float s in new[] { -1f, 1f })
{
    var pole = o + new Vector3(s * 5.0f, 0f, -9.9f);
    d.AddBox(pole + Vector3.up * 3f, new Vector3(0.14f, 6f, 0.14f), Mat.Wood);
    d.AddBox(pole + new Vector3(0f, 5.8f, 0.6f), new Vector3(0.08f, 0.08f, 1.3f), Mat.Wood);
    Props.Banner(lights, pole + new Vector3(0f, 5.7f, 0.65f), Vector3.left * s, s > 0 ? new Color(0.45f, 0.08f, 0.08f) : new Color(0.12f, 0.12f, 0.3f));
}
```
The poles now flank the plaza mouth, clear of the stalls, with banners facing the road.

### 3. The cat's route runs through props
`CampCat.cs:18-21`, legs between waypoints (S-units × 1.35).
- Leg P4 (3.24, 17.55) to P5 (4.32, 5.4) passes 0.36 m from the brazier at (4.32, 9.45).
- Leg P5 to P6 (4.86, -4.32) passes 0.37 m from the brazier at (4.32, -1.35).
- P5 sits 0.1 m from the lamp base at (4.6, 5), and P6 touches the lamp base at (4.6, -4).
- Leg P1 (-2.97, -3.38) to P2 (-6.21, 7.56) crosses the planter at (-6.21, 6.21) at x=-5.8.
- The cat prefab is also spawned inside the well, at (0.81, -10.8), in `DungeonDirector.cs:693`.

**Fix.** Keep the cat on the road lanes:
```csharp
private static readonly Vector3[] Route =
{
    new Vector3(2.0f, 0f, -6.5f), new Vector3(-2.0f, 0f, -2.0f), new Vector3(-2.0f, 0f, 6.0f), new Vector3(-1.5f, 0f, 13.5f),
    new Vector3(2.0f, 0f, 18.5f), new Vector3(2.0f, 0f, 8.0f), new Vector3(2.0f, 0f, -1.5f),
};
```
At `DungeonDirector.cs:693`, change the spawn to `Lobby.Origin + new Vector3(2.0f, 0f, -6.5f)`.

### 4. Crate and barrel clusters overlap each other
I replayed the `System.Random(7717)` scatter (`Lobby.cs:497-506`) and checked the positions.
- At (-5.4, -28.35): the barrel at (-5.25, -28.95) is inside the crate at (-5.3, -28.5). The other barrel overlaps it too (`plan_SW` 1478,1432).
- At (14.85, -14.85): the two crates and the barrel interpenetrate (`plan_SE` ~850,865).
- At (9.45, 24.3), near the hay: the barrel at (9.8, 24.96) is inside the crate at (9.54, 24.92).
- At (-16.2, -2.7) in the west: it is wedged against the mannequin base. Please check this one against the west audit.

**Fix.** Replace the loop body with a fixed, non-overlapping group:
```csharp
var at = o + S(x, 0f, z);
Props.Crate(d, at + new Vector3(-0.65f, 0f, -0.1f), 0.9f, 12f);
Props.Crate(d, at + new Vector3(0.65f, 0f, 0.2f), 0.7f, 38f);
Props.Barrel(d, at + new Vector3(0.1f, 0f, -1.1f));
```
Then delete `var rng = new System.Random(7717);`.

### 5. Orphaned yard props in the south-east
`Lobby.cs:898-900`, inside `BuildYard`, place two crates and a barrel at S(14..15.2, -12..-14), which is (18.9, -18.9), (20.5, -17.3) and (18.6, -16.5). The yard is in the west, so these are left over from the old layout. They are the "crates in front" in `eye_63_wall135_in.png` and show in `plan_SE` at about (1045,1043), (1115,975) and (1032,937).

**Fix.** Delete lines 898-900.

### 6. The keep box sticks out through the curtain wall
`Lobby.cs:326-327`: the keep body spans z -37..-31, but the wall's outer face is at -36.6 at the centre and -35.7 at x=±8. The back corners poke out up to 1.3 m. `plan_all` shows the block's bottom edge straight where the wall arcs.

**Fix.**
```csharp
s.AddBox(c + new Vector3(0f, 9f, -1f), new Vector3(16f, 18f, 4f), Mat.StoneWall);
s.AddBox(c + new Vector3(0f, 14f, -1.25f), new Vector3(10f, 10f, 3.5f), Mat.StoneWall);
```
The back face is then at z=-35.0, inside the wall thickness, and the front face stays at -31.

### 7. Keep windows half-buried in the corner turrets
At line 575, the windows at (±6.2, 4.5) and (±6.2, 12.5) cover x 5.85-6.55. The turret (x=±7.5, r=1.4) starts at 6.18 at that depth, so about 0.37 m of each window is buried. This comes from the geometry, not from a render.

**Fix.** Change `-6.2f`/`6.2f` to `-5.6f`/`5.6f` in the four entries that use them.

### 8. Spawn points stand on the log axes
`SpawnFor` (lines 183-187) puts players at 45°, 135°, 225° and 315°, radius 2.6, exactly on the four log axes. The log inner end is at radius 2.9, so players spawn nose to log.

**Fix.**
```csharp
float angle = clientId * Mathf.PI * 0.5f;
return Origin + S(0f, 0f, -14f) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.2f + Vector3.up * 0.1f;
```

### 9. The bonfire reads as a small brazier in a big ring (scale, lower priority)
The fire is `Props.Brazier` at lines 229-235: a bowl 1 m across and a flame 0.5 m high inside a 2.6 m stone ring with 1.8 m radial logs (`eye_41_well_top.png`, top). Replace line 235:
```csharp
var bed = MeshKit.Part(lights, "EmberBed", MeshKit.Cylinder(1.0f, 1.1f, 0.14f, 14), Mat.Glow, fire);
ArtLibrary.SetGlow(bed.GetComponent<Renderer>(), new Color(1f, 0.35f, 0.08f) * 0.6f);
for (int i = 0; i < 6; i++) { var q = Quaternion.Euler(0f, i * 60f, 0f); dressing.AddBox(fire + q * new Vector3(0f, 0.5f, 0.38f), new Vector3(0.2f, 0.2f, 1.5f), Mat.Wood, new Color(0.18f, 0.12f, 0.09f), q * Quaternion.Euler(38f, 0f, 0f)); }
Props.AddFlame(lights, fire + Vector3.up * 0.5f, 2.4f, new Color(1f, 0.55f, 0.25f), 12f, 16f, true);
```
I did not render-test the snippet.

### 10. Feed trough floats (gate side, check with the north audit)
`Lobby.cs:572` places a 0.5 m tall box with its centre at y=0.4, so it floats 15 cm. Change `S(-4.2f, 0.4f, 22.5f)` to `S(-4.2f, 0.25f, 22.5f)`. The oak at S(-2.8, 22.6), which is (-3.78, 30.5), has its trunk 0.8 m from the road edge and its crown overhangs the road and the trough (`plan_all` ~865,245).

### 11. Hay bales
- The barrel at (6.75, 23.49) clips bale 1 at (7.56, 23.49) by about 0.1 m.
- Bale 3 at (6.35, 29.16) is 3.9 m from the others, so it looks stray.
- After fix 4, change the barrel to S(4.4f, 0f, 19.0f) and bale 3 to S(4.9f, 0.25f, 20.2f) so the group is tight.

### 12. Empty south-east corner
After fixes 5 and 6, x 10-28, z -20..-31 is empty (`plan_SE`) while the plaza ring is full. If you want it filled, put the relocated yard crate pile there. I did not specify it.

## What looks right and should be left alone

- The well's structure. The posts sit in the rim, the torch clears the roof, and the roof is a clean pyramid. Only its position was off.
- The keep facade, rose window, doors, banners, spires and the six towers. Two towers flank the gate and the south ones clear the keep by 6 m.
- Wall torches and banners at 22.5° + 45°n, and the buttresses.
- The kerbs and the gravel strip itself (width 5.9 m).
- The bench ring orientation (benches face the fire) and bench height.
- The stone planters' construction.
- The flower planters' construction, and the planter pair at z=-2.43, which sits cleanly in the stall gap.
- Oak and yew construction. The two south oaks at (8.64, -12.96) and (-9.18, -15.12) clear every building. Their crowns only overhang the stone planters, which is fine.
- The Records slab, bounty board and hay bale geometry (bales rest on the ground).
- Crates and barrels at S(11,-11) and S(-4,-21) as locations (only their scatter was bad).
- The south wall stretch between the keep and the towers. The only things on it are one torch and banner each side.

Files: `C:/Users/User/Desktop/game_prototype/Deepbound/Assets/Scripts/World/Lobby.cs`, `.../World/CampCat.cs`, `.../World/DungeonDirector.cs` (line 693), `.../Networking/CampTour.cs`.

## Other open work saved for later
- Side quests (fetch / hunt / escort): a builder agent was cut off by the usage limit; its partial work is in the worktree branch `worktree-agent-a74a2105a6be7e696` (`.claude/worktrees/agent-a74a2105a6be7e696`). Resume it or redo from the brief.

## Round 4 (2026-11-08): the user's list, checked in windowed shots (`-talkshots`, camp_1-9 and casino_0-7)
- FIXED, seen: Silas's wares stay on the stall while he is away (camp_7). Brother Aldric kneels on the dais, not inside the shrine (camp_5). The gong's banner has its own pole beside the gong (camp_6).
- FIXED, seen: the tailor shop is rebuilt with a standing MIRROR that shows you (camp_1-3). The first render was nearly black, so a lantern and a point light now light the reflection.
- FIXED, seen: a pet sits beside you when you sit (camp_8/9; the log gives the kitten's offset as 0.6 m to the side).
  - Pets used to perch on tables in the tent (casino_2, casino_6). They now refuse ground more than 0.4 m above your feet and pick the other side.
- NEW, seen: the Bones panel with real pip dice on the baize (casino_2), the coffer's three padlocks (casino_3), the reels' pot + nudge card (casino_5), and the Skull Poker table and seated panel (casino_6/7).
  - The reels' result card is hidden while another casino panel is open (it overlapped the poker panel).
  - Interact prompts are hidden while the Bones or poker panel is open (the blackjack prompt showed through).
- NOT SEEN in a window:
  - Corwin riding the carriage away (needs a departure)
  - a real two-player Skull Poker hand (needs two players; the smoke test plays one server-side)
  - the quest cargo, finds and marks on a floor (smoke-tested only)
- Poker table spot: `Lobby.PokerSpot` = TentCenter + (1.0, 0, 2.2), between the coffer and the reels; the stools have room.

## Round 6 (2026-11-08): the tavern regular and the board of the fallen, checked in `-talkshots` (tavern_1/2)
- NEW, seen: OLD HOLLIS leans on the customer side of the bar at TavernCenter + (-1.6, 0, 1.85), facing the counter, clear of Marta's prompt and the tables (tavern_1). His speech card sits beside Marta's without covering it.
- NEW, seen: the BOARD OF THE FALLEN on the tavern's south wall at TavernCenter + (-3, 1.6, -3.3), 0.8 m from the west wall, clear of the kegs and the first window (tavern_2). The names show over it within 3.2 m. Each lost party gets a lit candle and a brass plate.
- FIXED, seen in ix_19-21/22-24: puzzle-room parts laid out in world coordinates (sigil stone, chime rings, lens, crystal) sat a whole room-offset away under a root at the coffer; the root is at the origin now.
- Still not seen in a window: a real wipe filling the board (tests never write the records; the shot uses made-up names).

## Round 7 (2026-11-09): stalls rebuilt, Bones gone, quieter rain (talk shots stall_1-3, weather_1)
- NEW, seen: Silas's stall (stall_1) has a back wall of shelves with coloured draughts, herbs from the beam, a valance and a visible lantern. Hobb's (stall_2) has a planked counter with a cloth runner, a valance and crates. The forge (stall_3) has a stepped hood over the coals.
- Hobb's tool wall sits in shadow behind him from the road; it is there, but dim.
- Bones (the dice table) no longer stands in the tent; its spot is empty floor now.
- Rain (weather_1, mid-shower) is a faint drizzle: fewer, thinner, darker streaks.
- NEW, seen (stall_4): Brann's corner is lit by a lantern post, with a chalk DRILL board and an armour stand beside him. Before, he stood in the dark.
- NEW, NOT properly seen (stall_5): the framing is blocked by a menhir. Ysolde's table has a cloth, a scroll and an inkpot, there is a cold-flame brazier, and rune charms hang on a cord between two stones. Needs a better camera spot.
