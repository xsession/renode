# Adding a New CPU Architecture to Renode (tlib)

> **Worked example: AVR 8-bit (ATmega328P)**
>
> This document captures every lesson learned while porting the AVR 8-bit
> architecture from scratch.  It is a practical, *fix-by-fix* reference for
> anyone adding a new CPU core to Renode's **tlib** translation back-end.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Directory and File Checklist](#2-directory-and-file-checklist)
3. [C-side: tlib Architecture Files](#3-c-side-tlib-architecture-files)
   - [cpu.h](#31-cpuh)
   - [helper.h](#32-helperh)
   - [helper.c](#33-helperc)
   - [op_helper.c](#34-op_helperc)
   - [translate.c](#35-translatec)
   - [arch_callbacks.c](#36-arch_callbacksc)
   - [arch_exports.c](#37-arch_exportsc)
   - [CMakeLists.txt](#38-cmakeliststxt)
4. [C#-side: CPU Class](#4-c-side-cpu-class)
5. [Build System Integration](#5-build-system-integration)
6. [Platform Description (.repl)](#6-platform-description-repl)
7. [Renode Script (.resc)](#7-renode-script-resc)
8. [Common Pitfalls & Fixes](#8-common-pitfalls--fixes)
9. [Full Fix Log (AVR)](#9-full-fix-log-avr)
10. [Quick-Start Checklist](#10-quick-start-checklist)

---

## 1. Overview

Renode uses **tlib**, a QEMU-derived translation library, to execute guest
code.  Each supported CPU architecture lives in:

```
src/Infrastructure/src/Emulator/Cores/
├── tlib/arch/<arch>/          # C source – instruction decode, helpers
└── <Arch>/                    # C# source – Renode integration
```

A working architecture requires:
- **7 C files** (cpu.h, helper.h, helper.c, op_helper.c, translate.c,
  arch_callbacks.c, arch_exports.c) plus a CMakeLists.txt
- **1 C# partial class** inheriting `TranslationCPU`
- **Build system wiring** (`.csproj` EmbeddedResource, `build.sh` CORES array)
- **A `.repl` platform description** and a `.resc` script to run it

The *existing RISC-V implementation* is the best reference — it is clean,
well-documented, and exercises the full tlib API surface.

---

## 2. Directory and File Checklist

```
tlib/arch/<arch>/
    CMakeLists.txt       # cmake target: translate-<arch>-le.so (or -be.so)
    cpu.h                # CPUState, constants, inline helpers
    helper.h             # DEF_HELPER macros for TCG helpers
    helper.c             # cpu_init, cpu_reset, cpu_handle_mmu_fault, ...
    op_helper.c          # HELPER() implementations + softmmu includes
    translate.c          # Instruction decode → TCG IR
    arch_callbacks.c     # DEFAULT_*_HANDLER macros
    arch_exports.c       # tlib_* functions + EXC_* macros

Cores/<Arch>/
    <Arch>.cs            # C# CPU class (partial)
```

---

## 3. C-side: tlib Architecture Files

### 3.1 cpu.h

This is the *most important* file.  Many build and runtime failures trace
back to a missing or incorrectly ordered definition here.

#### Required defines (BEFORE `cpu-defs.h`)

```c
#define NB_MMU_MODES    1                 // number of MMU modes (1 for no-MMU)
#define TARGET_PAGE_BITS 8                // log2 of page size in bytes
#define TARGET_PHYS_ADDR_SPACE_BITS 24    // physical address width
#include "cpu-defs.h"
```

> **Lesson:** `TARGET_PAGE_BITS` determines the alignment requirement for
> *every* memory region in your `.repl` files.  For AVR we used 8 (256 B)
> so that I/O-space addresses like 0x800100 are page-aligned.  A value of
> 10 (1 KB) caused runtime page-alignment errors.

#### CPUState struct

Must contain `CPU_COMMON` as the *first* member:

```c
typedef struct CPUState {
    CPU_COMMON
    uint32_t r[32];   // general-purpose registers
    uint32_t pc;
    uint32_t sp;
    uint32_t sreg;
    // ... additional arch registers
} CPUState;
```

#### Required inline helpers

These are called from tlib's core code and must be `static inline` in cpu.h:

| Function | Purpose |
|---|---|
| `cpu_mmu_index(env)` | Returns the current MMU mode index (0 for no-MMU) |
| `cpu_interrupts_enabled(env)` | Returns 1 if interrupts are globally enabled |
| `cpu_has_work(env)` | Returns 1 if the CPU should wake from WFI |
| `cpu_get_tb_cpu_state(env, pc, cs_base, flags)` | Fills in pc/flags for TB lookup |
| `cpu_pc_from_tb(env, tb)` | Restores PC from a TranslationBlock |

#### Include order (critical!)

```c
#include "cpu-defs.h"          // 1. pulls in CPU_COMMON, target types

// ... CPUState struct here ... //

#include "cpu-all.h"           // 2. AFTER CPUState (uses sizeof CPUState)

typedef struct DisasContext {   // 3. MUST be a complete struct here
    struct DisasContextBase base;
} DisasContext;

#include "exec-all.h"          // 4. AFTER DisasContext
```

> **Pitfall:** If `DisasContext` is only forward-declared in cpu.h
> (or defined inside translate.c), `translate-all.c` cannot allocate it
> and you get *incomplete type* errors.

#### cpu_handle_mmu_fault signature

Must match the tlib 7-parameter version:

```c
int cpu_handle_mmu_fault(CPUState *env, target_ulong address,
                         int access_type, int mmu_idx,
                         int access_width, int no_page_fault,
                         target_phys_addr_t *paddr);
```

---

### 3.2 helper.h

```c
#include "def-helper.h"

DEF_HELPER_2(raise_exception, void, env, i32)
DEF_HELPER_1(sleep,           void, env)
// ... more helpers ...

#include "def-helper.h"
```

**Critical rules:**

1. **NO `#pragma once` / include guards.**  This file is deliberately included
   multiple times with different macro definitions to generate helper
   prototypes, inline stubs, and TCG wrappers.
2. **`env` must be the first argument** to every `DEF_HELPER`.  The count
   (`DEF_HELPER_N`) includes `env`.  E.g., a helper with one extra arg is
   `DEF_HELPER_2(..., env, i32)`.
3. Bookended by `#include "def-helper.h"` — the *second* include undefines
   the macros.

---

### 3.3 helper.c

Required functions:

```c
int  cpu_init(const char *cpu_model);          // returns 0 on success
void cpu_reset(CPUState *env);
void tlib_arch_dispose(void);

int  cpu_handle_mmu_fault(CPUState *env, target_ulong address,
         int access_type, int mmu_idx, int access_width,
         int no_page_fault, target_phys_addr_t *paddr);

target_phys_addr_t cpu_get_phys_page_debug(CPUState *env, target_ulong addr);

uint64_t cpu_get_state_for_memory_transaction(CPUState *env,
         target_ulong addr, int access_type);
```

For a no-MMU architecture, `cpu_handle_mmu_fault` does identity mapping:

```c
*paddr = address;
int prot = PAGE_READ | PAGE_WRITE | PAGE_EXEC;
tlb_set_page(env, address & TARGET_PAGE_MASK,
             address & TARGET_PAGE_MASK,
             prot, mmu_idx, TARGET_PAGE_SIZE);
return 0;
```

> **Lesson:** Forgetting `cpu_get_phys_page_debug()` or
> `cpu_get_state_for_memory_transaction()` causes *undefined symbol* errors
> at runtime when the `.so` is loaded.  These are called by the Renode C#
> host via NativeBinder.

---

### 3.4 op_helper.c

```c
#include "cpu.h"
#include "arch_callbacks.h"
#include "softmmu_exec.h"       // ← CRITICAL
#define HELPER_H
#include "helper.h"
```

> **Lesson: `#include "softmmu_exec.h"` is mandatory.**
> This header *generates* the `__ldq_mmu`, `__stq_mmu`, `__ldl_mmu`, … stub
> functions that tlib's core uses for data-space memory access.  Without it,
> the build succeeds but loading the `.so` at runtime fails with
> *undefined symbol: __ldq_mmu*.

Required implementations:

| Function | Purpose |
|---|---|
| `HELPER(raise_exception)(env, excp)` | Dispatch exception to Renode |
| `do_unaligned_access(...)` | Unaligned memory access fault |
| `arch_raise_mmu_fault_exception(...)` | MMU fault exception path |
| `arch_tlb_fill(...)` | TLB refill — usually delegates to `cpu_handle_mmu_fault` |
| `do_interrupt(env)` | Exception/interrupt vector dispatch |

All `HELPER()` function signatures **must** include `CPUState *env` as
the first parameter, matching the `DEF_HELPER` declarations.

---

### 3.5 translate.c

The instruction decoder.  Required framework hooks:

```c
void gen_sync_pc(DisasContext *dc);              // emit TCG to sync dc->pc → env->pc
void setup_disas_context(DisasContext *dc, CPUState *env);  // init dc at TB start
void gen_goto_tb(DisasContext *dc, int n, target_ulong dest); // emit TB chain / exit
```

Common mistakes:
- Calling `gen_helper_XXX(arg)` instead of `gen_helper_XXX(cpu_env, arg)` —
  the `cpu_env` TCG variable must be passed for every helper that takes `env`.
- Duplicate `case` values in instruction decode switch — C compilers reject this.
- Using interrupt constants (e.g. `CPU_INTERRUPT_RESET`) that are not defined
  in the tlib framework — use your own `EXCP_*` constants instead.

---

### 3.6 arch_callbacks.c

```c
#include "callbacks.h"
#include "arch_callbacks.h"

DEFAULT_INT_HANDLER1(int32_t tlib_find_best_interrupt, void)
DEFAULT_VOID_HANDLER1(void tlib_acknowledge_interrupt, int32_t interrupt_number)
DEFAULT_VOID_HANDLER0(void tlib_on_cpu_halted)
DEFAULT_VOID_HANDLER0(void tlib_on_cpu_power_down)
```

**Macro format:**

```
DEFAULT_INT_HANDLER1(return_type function_name, param_type)
                     ^^^^^^^^^^^ ^^^^^^^^^^^^^^  ^^^^^^^^^^
                     INCLUDES the return type as part of argument 1
```

> **Pitfall:** Passing only the function name
> (`DEFAULT_INT_HANDLER1(tlib_find_best_interrupt, void)`) causes
> *implicit function declaration* errors because the macro pastes the
> tokens as a prototype.

Must include `callbacks.h` for the macro definitions.

---

### 3.7 arch_exports.c

```c
#include <stdint.h>
#include "cpu.h"
#include "unwind.h"
#include "arch_exports.h"

void tlib_set_entry_point(uint32_t entry)
{
    cpu->pc = entry & ~1u;
}
EXC_VOID_1(tlib_set_entry_point, uint32_t, entry)
```

**Pattern:** Function body *first*, then the `EXC_*` macro on a separate line.

> **Pitfall:** Putting the function body inside the `EXC_*` macro, or omitting
> the `#include "unwind.h"`, both cause compilation errors.

---

### 3.8 CMakeLists.txt

Declare sources and the output library name:

```cmake
set(ARCH_SOURCES
    cpu.h
    helper.c
    helper.h
    op_helper.c
    translate.c
    arch_callbacks.c
    arch_exports.c
)

add_library(translate-avr-le SHARED ${ARCH_SOURCES})
target_link_libraries(translate-avr-le tlib-common)
```

The library name determines what the C# side embeds:
`translate-<arch>-<endian>.so`

---

## 4. C#-side: CPU Class

Create `Cores/<Arch>/<Arch>.cs`:

```csharp
using Antmicro.Renode.Core;
using Endianess = ELFSharp.ELF.Endianess;

namespace Antmicro.Renode.Peripherals.CPU
{
    [GPIO(NumberOfInputs = 1)]
    public partial class AVR : TranslationCPU
    {
        public AVR(string cpuType, IMachine machine)
            : base(cpuType, machine, Endianess.LittleEndian) { }

        // -------- REQUIRED abstract overrides --------
        public override string Architecture      => "avr";
        public override string GDBArchitecture   => "avr";
        public override string[] AllLLVMTriples  => new[] { "avr" };
        public override string LLVMModel         => "avr";
        public override string GetLLVMTriple(uint flags) => AllLLVMTriples[0];
        public override Endianess DisassemblyHexFormatting => Endianess.LittleEndian;

        // -------- Interrupt decode --------
        protected override Interrupt DecodeInterrupt(int number)
        {
            switch(number)
            {
            case 0:  return Interrupt.Hard;
            default: throw InvalidInterruptNumberException;
            }
        }

        // -------- Callbacks (C# ↔ C) --------
        [Export]
        private int FindBestInterrupt() => 0;

        [Export]
        private void AcknowledgeInterrupt(int interruptNumber) { }
    }
}
```

> **Lesson:** `TranslationCPU` has three abstract members that *must* be
> overridden or the C# build fails with ~3 errors:
> - `LLVMModel` (string property)
> - `GetLLVMTriple(uint flags)` (method)
> - `DisassemblyHexFormatting` (Endianess property)

---

## 5. Build System Integration

### 5.1 Add EmbeddedResource to `.csproj`

In `src/Infrastructure/Infrastructure_NET.csproj`:

```xml
<EmbeddedResource Include="path/to/translate-avr-le.so" />
```

The `translate-<arch>-<endian>.so` must be listed or Renode cannot load the
CPU at runtime.

### 5.2 Register in `build.sh` CORES array

In the root `build.sh`, add to the `CORES` array:

```bash
CORES=(arm arm-m arm64 i386 ppc ppc64 riscv riscv64 sparc x86_64 xtensa avr)
```

### 5.3 Build commands (WSL / Linux)

```bash
# First time: fetch vendored NuGet libraries
./tools/building/fetch_libraries.sh

# Full build (Debug, .NET)
./build.sh --net -d

# Subsequent builds (skip submodule/library fetch)
./build.sh --skip-fetch --net
```

### 5.4 CRLF / line-ending issues

If building from a Windows checkout (or WSL with Windows mount):

```bash
# Fix all .sh files
find . -name '*.sh' -exec sed -i 's/\r$//' {} +
```

> **Lesson:** `tools/building/createAssemblyInfo.sh` reads
> `tools/version` and cats it into a C# `AssemblyInfo.cs` file.
> If `version` has Windows CRLF line endings, the generated C# file
> contains a literal `\r` inside a string constant, causing:
> *error CS1010: Newline in constant*.
> Fix: add `tr -d '\r'` to the version-reading pipeline.

### 5.5 WSL / KVM build complications

On x86_64 WSL, `tools/common.sh` maps `uname -m` to `DETECTED_ARCH=i386`,
which triggers a KVM build that requires `libkvm.so`.  For simulation-only
builds, comment out the KVM block in `build.sh`:

```bash
# if [ "$ON_LINUX" = "true" ]; then
#     build_kvm
# fi
```

Or create dummy `.so` stubs if you only need the final Renode binary.

---

## 6. Platform Description (.repl)

### Minimal `.repl` for AVR (ATmega328P)

```
cpu: CPU.AVR @ sysbus

flash: Memory.MappedMemory @ sysbus 0x000000
    size: 0x8000

dataspace: Memory.MappedMemory @ sysbus 0x800000
    size: 0x900
```

### Page alignment rules

Every region's **base address** and **size** must be aligned to
`2^TARGET_PAGE_BITS` bytes.

| `TARGET_PAGE_BITS` | Page size | Alignment |
|---|---|---|
| 8 | 256 B | 0x100 |
| 10 | 1 KB | 0x400 |
| 12 | 4 KB | 0x1000 |

> **Lesson:** AVR I/O registers start at data-space offset 0x0020
> (bus address 0x800020).  With `TARGET_PAGE_BITS=10` (1 KB pages),
> 0x800020 is *not* page-aligned → runtime error.
> Switching to `TARGET_PAGE_BITS=8` (256 B pages) resolved this.

### Harvard architecture note

AVR uses Harvard architecture (separate program/data address spaces).
In Renode, we map them to a *single system bus* with an offset:

- **Program memory (flash):** at `0x000000`
- **Data memory (SRAM + I/O):** at `0x800000` (`AVR_DATA_BASE`)

The translate.c code uses `AVR_DATA_BASE` for all LD/ST operations.

---

## 7. Renode Script (.resc)

```
using sysbus

mach create "arduino_nano"
machine LoadPlatformDescription @scripts/single-node/arduino_nano_blink/nano_test.repl
sysbus LoadELF @scripts/single-node/arduino_nano_blink/blink.elf

emulation RunFor "00:00:05"
quit
```

Run headless:

```bash
mono output/bin/Debug/Renode.exe --console --disable-xwt \
    scripts/single-node/arduino_nano_blink/test_blink.resc
```

Or on .NET:

```bash
dotnet output/bin/Debug/net6.0/Renode.dll --console --disable-xwt \
    scripts/single-node/arduino_nano_blink/test_blink.resc
```

---

## 8. Common Pitfalls & Fixes

| # | Symptom | Root Cause | Fix |
|---|---|---|---|
| 1 | `error: incomplete type 'DisasContext'` in translate-all.c | `DisasContext` only forward-declared or defined in translate.c | Define full struct in **cpu.h** after `cpu-all.h` |
| 2 | `error: implicit declaration of function 'tlb_set_page'` | Missing `cpu-all.h` | Include `cpu-all.h` **after** CPUState struct in cpu.h |
| 3 | `error: unknown type name 'TranslationBlock'` | Missing `exec-all.h` | Include `exec-all.h` **after** DisasContext in cpu.h |
| 4 | `undefined symbol: __ldq_mmu` at runtime | op_helper.c missing softmmu include | Add `#include "softmmu_exec.h"` to op_helper.c |
| 5 | `undefined symbol: cpu_get_phys_page_debug` at runtime | Function not implemented | Add to helper.c (identity mapping for no-MMU) |
| 6 | `undefined symbol: cpu_get_state_for_memory_transaction` at runtime | Function not implemented | Add to helper.c (return 0 for simple arch) |
| 7 | `error CS1010: Newline in constant` in AssemblyInfo.cs | CRLF in `tools/version` file | `tr -d '\r'` in createAssemblyInfo.sh |
| 8 | `does not implement inherited abstract member 'LLVMModel'` | Missing C# override | Add `LLVMModel`, `GetLLVMTriple(uint)`, `DisassemblyHexFormatting` |
| 9 | `helper.h: redefinition of 'gen_helper_XXX'` | `#pragma once` in helper.h | **Remove** pragma — file is included multiple times intentionally |
| 10 | `error: too many/few arguments to 'DEFAULT_INT_HANDLER1'` | Wrong macro argument format | Use `DEFAULT_INT_HANDLER1(int32_t func_name, param_type)` |
| 11 | `Unexpected token` in arch_exports.c | Function body inside EXC_ macro | Body **before** macro: `void f() { ... }` then `EXC_VOID_0(f)` |
| 12 | Page alignment error for 0x800100 | `TARGET_PAGE_BITS=10` → 1 KB pages | Change to `TARGET_PAGE_BITS=8` (256-byte pages) for AVR |
| 13 | Build fails looking for libkvm.so on WSL x86_64 | `common.sh` maps x86_64 → i386, enables KVM build | Comment out KVM block in build.sh or provide dummy stubs |

---

## 9. Full Fix Log (AVR)

This section documents every modification made, in the order fixes were
applied, to get a working AVR core from an initial non-compiling codebase.

### Phase 1: Compilation Fixes (tlib C code)

1. **cpu.h** — Added `NB_MMU_MODES`, `TARGET_PAGE_BITS`, `TARGET_PHYS_ADDR_SPACE_BITS`
   before `cpu-defs.h`
2. **cpu.h** — Added `#include "cpu-all.h"` after CPUState
3. **cpu.h** — Moved `DisasContext` from translate.c to cpu.h as full struct definition
4. **cpu.h** — Added `#include "exec-all.h"` after DisasContext
5. **cpu.h** — Added all 5 required inline helpers
6. **cpu.h** — Updated `cpu_handle_mmu_fault` to 7-parameter signature
7. **helper.h** — Removed `#pragma once`; added `env` to all DEF_HELPER macros;
   added closing `#include "def-helper.h"`
8. **helper.c** — Changed `cpu_init` to return `int` (was returning `CPUState*`);
   added `cpu_get_phys_page_debug()`; added `cpu_get_state_for_memory_transaction()`
9. **op_helper.c** — Added `#include "softmmu_exec.h"`; added `CPUState *env`
   to all HELPER function signatures; added `arch_tlb_fill()` and
   `arch_raise_mmu_fault_exception()`
10. **arch_callbacks.c** — Fixed macro calls to include return types;
    added `#include "callbacks.h"`
11. **arch_exports.c** — Changed to body-before-EXC-macro pattern;
    added `#include "unwind.h"` and `<stdint.h>`
12. **translate.c** — Added `gen_sync_pc()`, `setup_disas_context()`,
    `gen_goto_tb()`; changed `gen_helper_*()` calls to pass `cpu_env`;
    removed undefined `CPU_INTERRUPT_RESET`; fixed duplicate case values

### Phase 2: C# Build Fixes

13. **AVR.cs** — Added `LLVMModel => "avr"`, `GetLLVMTriple(uint) => AllLLVMTriples[0]`,
    `DisassemblyHexFormatting => Endianess.LittleEndian`
14. **Infrastructure_NET.csproj** — Added `<EmbeddedResource>` for `translate-avr-le.so`
15. **createAssemblyInfo.sh** — Added `tr -d '\r'` to strip CRLF from version string

### Phase 3: Build System Fixes

16. **build.sh** — Commented out KVM/virt build block; added `avr` to CORES array
17. **fetch_libraries.sh** — Ran to populate `lib/resources/libraries/` with
    vendored DLLs (IronPython, protobuf-net, Nini, Sprache, etc.)

### Phase 4: Runtime Fixes

18. **cpu.h** — Changed `TARGET_PAGE_BITS` from 10 to 8 (256-byte pages) to
    fix page-alignment errors with AVR I/O address space
19. **nano_test.repl** — Simplified to single dataspace region at 0x800000
    with size 0x900 (page-aligned)

Total: **19 distinct fixes** across 12 files to go from non-compiling to
successful simulation.

---

## 10. Quick-Start Checklist

When adding a brand new architecture, work through this list in order:

- [ ] Create `tlib/arch/<arch>/` directory with all 7 C files + CMakeLists.txt
- [ ] Verify cpu.h define/include order: defines → cpu-defs.h → CPUState → cpu-all.h → DisasContext → exec-all.h
- [ ] Verify cpu.h has all 5 inline helpers + 7-param `cpu_handle_mmu_fault` declaration
- [ ] Verify helper.h has NO include guards, `env` in every DEF_HELPER, bookended by `def-helper.h`
- [ ] Verify helper.c has `cpu_init` (returns int), `cpu_reset`, `cpu_handle_mmu_fault`, `cpu_get_phys_page_debug`, `cpu_get_state_for_memory_transaction`
- [ ] Verify op_helper.c includes `softmmu_exec.h` and all HELPER() funcs take `CPUState *env`
- [ ] Verify arch_callbacks.c uses correct macro format (includes return type)
- [ ] Verify arch_exports.c uses body-before-EXC-macro pattern
- [ ] Create C# class with all 3 abstract overrides (`LLVMModel`, `GetLLVMTriple`, `DisassemblyHexFormatting`)
- [ ] Add `.so` as `<EmbeddedResource>` in Infrastructure_NET.csproj
- [ ] Add arch to CORES array in build.sh
- [ ] Strip CRLF from all .sh files if building on Windows
- [ ] Choose `TARGET_PAGE_BITS` to match your memory map alignment needs
- [ ] Create `.repl` with all regions page-aligned
- [ ] Build, load, and test with a minimal firmware (e.g., blink LED)

---

*Document generated from practical experience adding AVR 8-bit support to
Renode.  Every fix listed was discovered through compilation errors, linker
errors, or runtime failures — not from documentation.*
