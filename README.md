# Steel Frame Optimization with Composite Tube Columns (ETABS)

> **Status: in development (0.5.1).** This project starts from
> [SteelFamewithCompositeColumn_ETABS](https://github.com/ibrahimaydogdu/SteelFamewithCompositeColumn_ETABS) (2026.10.3).
> Added so far: Social Spider Optimization (16th method), concrete-filled tube columns and hybrid columns (steel or composite per group, optimized transition story). Planned:
> - a separate composite floor design program is proposed for the end of the project.
>
> Also in this repository: **[ModelBuilder](ModelBuilder/README.md)** (Turkish), a separate program that builds the ETABS models of the examples from a CSV table (ASCE 7-22 loads and combinations, section pools, member groups) for use with the optimization program.
>
> The text below describes the inherited features.

Discrete sizing optimization of 3D steel frames with optional concrete-encased composite columns. The program drives
ETABS through its API: every candidate design is analysed and designed by ETABS. The W sections of the design
groups are chosen by a metaheuristic to minimize steel weight, or the total cost of steel, rebar, concrete and
formwork in composite mode.

*Türkçe kullanım kılavuzu: [KULLANIM_KILAVUZU.md](KULLANIM_KILAVUZU.md)*

## Features
- **16 optimization methods:**
  - Harmony Search
  - Biogeography-Based Optimization
  - Whale Optimization
  - Dandelion Optimizer
  - Artificial Bee Colony
  - Ant Colony Optimization
  - Brain Storm Optimization
  - Crow Search
  - Firefly Algorithm
  - Grasshopper Optimization
  - Teaching-Learning-Based Optimization (with an optional Harmony Search phase)
  - Tree-Seed Algorithm
  - Grey Wolf Optimizer
  - Honey Badger Algorithm
  - Aquila Optimizer
  - Social Spider Optimization (translated from the author's Fortran code)
- **Constraints:**
  - ETABS steel design (AISC 360-22 / 360-16 / 360-10);
  - inter-story and top drift, with service lateral cases and seismic drift amplification (Cd/Ie or R/I);
  - column-to-column and beam-to-column geometry;
  - automatic repair of violated constraints.
- **Hybrid columns:** every column group steel or composite (fixed by the user or optimized); transition variable per column stack ("composite up to a story, steel above") or type variable per group.
- **Concrete-filled tube columns (AISC 360-16 / 360-22 I2.2):**
  - square HSS boxes of the CSI section library and built-up boxes up to 1000 mm, optional rectangular boxes and pipes (`TubeSections.xml`);
  - the fill concrete is the concrete of the model;
  - seismic wall slenderness limits of TBDY 2018 Table 9.3 / AISC 341 (high ductility by default);
  - internal check during the search, ETABS filled tube sections and composite column design for the final design.
- **Encased composite columns (AISC 360-16 / 360-22):**
  - a fast internal check is used during the search;
  - the final design is verified with ETABS composite column design, and columns that fail are made larger automatically;
  - a calibration factor for the internal check is suggested from the ETABS results.
- **Analysis:**
  - P-Delta analysis;
  - only the load cases used by the checks are solved;
  - repeated designs are read from a result cache;
  - ETABS is restarted periodically to release memory.
- **Robust long runs:**
  - the run works in the background and the form shows the current phase;
  - Stop / resume;
  - a safe backup every loop and every 10 minutes;
  - when resuming, the backup is checked against the model (SHA-256, groups, section library).
- **Outputs:**
  - result XML;
  - Excel workbook (summary, cost breakdown per group, design, governing constraints, ETABS composite check, convergence history);
  - the best design as `<model>_best.EDB`.

## Requirements
- Windows 10/11 (64 bit).
- ETABS 22 (tested with 22.6) or ETABS 19. ETABS composite verification needs ETABS 20 or later.
- .NET Framework 4.7.2.
- Visual Studio 2022 or later to build. The NuGet package `DocumentFormat.OpenXml` 2.18 is restored automatically.

## Build and run
1. Open `SteelFrameWithCompositeTubeColumnsETABS.sln` in Visual Studio and build (*Build > Build Solution*).
2. The program is `bin\Debug\FrameSap2000.exe`. Check the paths in `FrameSap2000.exe.config` (ETABS program, section library).
3. Prepare the ETABS model:
   - steel groups as design variables (*Steel Frame Design*);
   - load pattern types (Dead, Live, Wind, Quake);
   - strength combinations, or enable *Create default design combos*.
4. Select the model and an output file, choose the method and the number of analyses, and press **Start**.

Keep the program and the models in a short folder path. The Windows 260 character limit can stop the configuration
file or the Excel library from loading.

See the [user guide](KULLANIM_KILAVUZU.md) for the settings, method parameters and recommended run sizes, and for how
to read the results.

## Documentation (Turkish)
| File | Content |
|---|---|
| [YETENEKLER_RAPORU.md](YETENEKLER_RAPORU.md) | Capabilities report: what the program can and cannot do, results so far, options for the paper scope |
| [KULLANIM_KILAVUZU.md](KULLANIM_KILAVUZU.md) | User guide |
| [ModelBuilder/README.md](ModelBuilder/README.md) | Model builder: CSV table, generated model, loads, combinations, limits |
| [PROGRAM_KURALLARI.md](PROGRAM_KURALLARI.md) | Program structure and rules for developers |
| [DEGISIKLIKLER.md](DEGISIKLIKLER.md) | Change log with test results |
| [Ajan/](Ajan/README.md) | Agent workflow: capabilities, memory, task queue |
| [KOD_INCELEME_RAPORU.md](KOD_INCELEME_RAPORU.md) | Code review report |
| [AISC360_22_Composite_Column_Rules.md](AISC360_22_Composite_Column_Rules.md), [AISC360_16_Composite_Column_Rules.md](AISC360_16_Composite_Column_Rules.md) | Composite column rules used by the internal check |

## Notes
- The program never modifies the selected model. All analyses run on a working copy in `%TEMP%\SteelFrameOpt`.
- Two runs with the same seed may differ slightly: tiny numerical differences in the ETABS P-Delta solution can change the search path.
- ETABS is a product of Computers and Structures, Inc. and needs its own licence. It is not part of this repository.

## Citation
If you use this program in academic work, please cite the repository:
`Aydogdu, I., Steel Frame Optimization with Composite Tube Columns (ETABS), https://github.com/ibrahimaydogdu/SteelFamewithCompositeTubeColumn_ETABS`

## License
[MIT](LICENSE)
