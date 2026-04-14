# Adding a New CPU Core to the Renode Fork — Step-by-Step Tutorial

This tutorial documents the exact procedure used to add new CPU architecture cores
(dsPIC33, C2000, AVR, STM8, PIC16, PIC18) to this Renode fork.  Follow every step in
the order presented; each step builds on the previous one.

---

## Overview of the Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  Renode (C# / .NET)                                                 │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  Cores/{ARCH}/{ARCH}.cs          — TranslationCPU subclass   │   │
│  │  Cores/{ARCH}/{ARCH}Registers.cs — register binding          │   │
│  │  Peripherals/…/{ARCH}_*.cs       — peripheral models         │   │
│  │  platforms/cpus/*.repl            — platform descriptions     │   │
│  └────────────────────────┬─────────────────────────────────────┘   │
│                           │  P/Invoke (dlopen translate-{arch}.so)  │
└───────────────────────────┼─────────────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────────────┐
│  tlib (C — Tiny Code Generator / TCG)                               │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  tlib/arch/{arch}/cpu.h          — CPUState struct, EXCP_*   │   │
│  │  tlib/arch/{arch}/cpu_registers.h/c — register enum + getter │   │
│  │  tlib/arch/{arch}/arch_callbacks.h/c — C→C# callbacks        │   │
│  │  tlib/arch/{arch}/arch_exports.h/c   — C#→C entry points     │   │
│  │  tlib/arch/{arch}/helper.h/c     — cpu_reset, cpu_init, …    │   │
│  │  tlib/arch/{arch}/op_helper.c    — HELPER() implementations  │   │
│  │  tlib/arch/{arch}/translate.c    — TCG instruction translator │   │
│  └──────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  renode/arch/{arch}/renode_{arch}_callbacks.c  — bridge glue │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

**Key concepts:**

- **tlib** is the JIT translation engine; it runs the architectural C code and produces
  native machine code via the TCG (Tiny Code Generator) intermediate representation.
- **`translate.c`** is the hot path: it decodes one instruction at a time and emits TCG
  *ops* (abstract RISC-style micro-operations) into a translation block (TB).
- **`op_helper.c`** provides `HELPER()` C functions called by the emitted TCG code for
  complex or rarely taken paths (exceptions, memory-mapped I/O helpers, etc.).
- **The C# layer** wraps the compiled `.so` through P/Invoke, provides the clock model,
  handles interrupts through a priority queue, and exposes registers to GDB.

---

## Step 1 — Plan the Architecture

Before writing any code, answer these questions:

| Question | Example (AVR) | Example (PIC18) |
|---|---|---|
| Data bus width | 8-bit | 8-bit |
| Instruction word size | 16-bit (14-bit code) | 16-bit (some 32-bit) |
| Endianness | Little-endian | Little-endian |
| Program memory base | `0x000000` | `0x000000` |
| Data space base | `0x800000` (Harvard) | `0xA00000` (Harvard) |
| Status/flag register | `SREG` byte | `STATUS` byte |
| Interrupt enable bit | `SREG_I` (bit 7) | `INTCON.GIEH/GIEL` |
| Reset vector | address `0` | address `0x000000` |
| Interrupt vector layout | `N × 4` bytes from `0` | high=`0x8`, low=`0x18` |
| Total architectural registers | 37 | 14 |

Write down: CPUState fields, EXCP_* numbers, STATUS bit mask, interrupt model.

---

## Step 2 — `tlib/arch/{arch}/cpu.h`

This is the most important tlib file. It defines:

1. `CPUState` struct — all architectural registers as `uint32_t` fields.
2. `EXCP_RESET`, `EXCP_INT_*`, `EXCP_UNDEF` constants.
3. Status bit `#define`s.
4. The `cpu_interrupts_enabled(env)` inline macro — returns `1` when maskable interrupts
   are enabled (checked every TB before calling `process_interrupt()`).
5. `#include "cpu-base.h"` — must be **last**.
6. `static inline int cpu_mmu_index(CPUState *env, bool ifetch) { return 0; }` for flat
   address spaces.

**Pattern for Harvard architectures:**
```c
#define MY_DATA_BASE  0x800000u   /* bias added to all data-space addresses */
```
All data-space `qemu_ld*/qemu_st*` TCG ops must add this offset so they hit the correct
region of the flat address map exported to C# via `sysbus`.

---

## Step 3 — `cpu_registers.h` and `cpu_registers.c`

### `cpu_registers.h`
Define a C `enum` (typedef) listing every register visible to GDB / Renode:
```c
typedef enum {
    R0 = 0, R1, … PC = 32, SREG = 33, SP = 34,
    MY_REGISTERS_COUNT = 35
} MyRegisters;
```

### `cpu_registers.c`
Implement `get_reg_pointer_32()`:
```c
uint32_t *get_reg_pointer_32(CPUState *env, uint8_t reg)
{
    switch(reg) {
    case PC:   return &env->pc;
    case SREG: return &env->sreg;
    /* … */
    default:   return NULL;
    }
}
CPU_REGISTER_ACCESSOR(32)   /* generates read_register / write_register */
```
`CPU_REGISTER_ACCESSOR(32)` is a tlib macro that fills in the generic
`read_register(n)` / `write_register(n, v)` functions used by the C# layer.

**Split registers** (e.g. STM8 XH/XL): use a small `sync_split_regs()` helper that
extracts byte halves from the parent 16-bit field into scratch `uint32_t` locals before
returning their address.

---

## Step 4 — `arch_callbacks.h` and `arch_callbacks.c`

These files declare and stub out the four callbacks that tlib calls back into C#:

```c
/* arch_callbacks.h */
void tlib_find_best_interrupt(void);        /* ask C# for pending interrupt # */
void tlib_acknowledge_interrupt(int32_t n); /* tell C# interrupt was taken    */
void tlib_on_cpu_halted(void);              /* SLEEP/HALT instruction hit      */
void tlib_on_cpu_power_down(void);          /* deep-sleep / power-off          */

/* arch_callbacks.c */
DEFAULT_INT_HANDLER1(tlib_find_best_interrupt)
DEFAULT_VOID_HANDLER1(tlib_acknowledge_interrupt, int32_t)
DEFAULT_VOID_HANDLER0(tlib_on_cpu_halted)
DEFAULT_VOID_HANDLER0(tlib_on_cpu_power_down)
```
The `DEFAULT_*` macros (from `callbacks.h`) generate weak no-op implementations that are
overridden by the real C# bindings at runtime.

---

## Step 5 — `arch_exports.h` and `arch_exports.c`

Declare and implement functions that C# calls **into** tlib:

```c
/* arch_exports.h */
void tlib_set_entry_point(uint32_t address);
void tlib_clear_wfi(void);
void tlib_set_wfi(void);

/* arch_exports.c */
EXC_VOID_1(tlib_set_entry_point, uint32_t, address)
{
    env->pc = address & 0x1FFFu;  /* mask to valid width */
}
EXC_VOID_0(tlib_clear_wfi) { env->halted = 0; }
EXC_VOID_0(tlib_set_wfi)   { env->halted = 1; }
```
`EXC_VOID_N` macros (from `exports.h`) handle the thread-safety lock/unlock around
tlib state access.

---

## Step 6 — `helper.h`

Declare all slow-path helper functions using the `DEF_HELPER_*` macro system:

```c
#include "def-helper.h"

DEF_HELPER_1(raise_exception, void, i32)   /* called by TCG for EXCP_* */
DEF_HELPER_0(sleep,           void)        /* SLEEP / WFI instruction  */
DEF_HELPER_0(wdt_reset,       void)        /* watchdog reset            */

#include "def-helper.h"           /* second include closes the macros */
```
These macros generate both the C prototype and the TCG helper registration table entry.

---

## Step 7 — `helper.c`

Implement the lifecycle functions:

```c
CPUState *cpu_init(const char *cpu_model)
{
    return cpu_generic_init(cpu_model, DATA_BUS_WIDTH_IN_BITS);
}

void cpu_reset(CPUState *env)
{
    memset(env, 0, sizeof(*env));
    /* set reset-state register values, e.g.: */
    env->pc   = 0;
    env->sreg = 0;
}

int cpu_handle_mmu_fault(CPUState *env, target_ulong addr, int rw, int mmu_idx)
{
    env->exception_index = EXCP_UNDEF;
    cpu_loop_exit(env);
    return 0;
}

void tlib_arch_dispose(void) {}
```

---

## Step 8 — `op_helper.c`

Implement the `HELPER()` bodies and the interrupt entry/dispatch function:

```c
void HELPER(raise_exception)(uint32_t excp)
{
    env->exception_index = excp;
    cpu_loop_exit(env);           /* longjmp out of TCG execution */
}

void HELPER(sleep)(void)
{
    env->halted = 1;
    tlib_on_cpu_halted();
    env->exception_index = EXCP_RESET;
    cpu_loop_exit(env);
}

/* Called by process_interrupt() when an interrupt is pending */
void do_interrupt(CPUState *env)
{
    /* 1. Disable global interrupt enable so we don't re-enter */
    env->sreg &= ~SREG_I;

    /* 2. Save return state (push PC, push status, etc.)
     *    Use tcg_gen_qemu_st8/st16 helpers or direct memory writes
     *    depending on whether we are inside or outside a TB. */
    uint32_t sp = env->sp;
    stb_data(AVR_DATA_BASE + sp,   env->pc & 0xFF);        sp--;
    stb_data(AVR_DATA_BASE + sp,   (env->pc >> 8) & 0xFF); sp--;
    env->sp = sp;

    /* 3. Jump to vector */
    env->pc = env->exception_index * 4;
    env->exception_index = -1;
}

void process_interrupt(CPUState *env, int current_instructions)
{
    if(!cpu_interrupts_enabled(env)) return;
    tlib_find_best_interrupt();           /* sets env->exception_index */
    if(env->exception_index >= 0)
        do_interrupt(env);
}
```

**Important:** `tlib_find_best_interrupt()` is implemented by C# and sets
`env->exception_index` to the interrupt number (or -1 if none). Renode feeds interrupt
signals into the CPU via GPIO wires connected to `cpu@N` in the `.repl` file.

---

## Step 9 — `translate.c`

This is the largest file. Structure:

```
translate_init()           — create TCG globals (one per CPU register)
disas_{arch}_insn()        — decode one instruction, emit TCG ops
gen_intermediate_code()    — outer loop: call disas until end of TB
restore_state_to_opc()     — recover PC for exception reporting
process_interrupt()        — check + dispatch pending interrupt
```

### 9.1 TCG globals

```c
static TCGv cpu_pc, cpu_sreg, cpu_r[32];

static void translate_init(void)
{
    static int done = 0; if(done) return; done = 1;
    cpu_pc   = tcg_global_mem_new_i32(TCG_AREG0, offsetof(CPUState, pc),   "PC");
    cpu_sreg = tcg_global_mem_new_i32(TCG_AREG0, offsetof(CPUState, sreg), "SREG");
    for(int i = 0; i < 32; i++) {
        char name[4]; snprintf(name, 4, "r%d", i);
        cpu_r[i] = tcg_global_mem_new_i32(TCG_AREG0, offsetof(CPUState, r[i]), name);
    }
}
```

### 9.2 Instruction fetch

- Byte-addressed program memory: use `lduw_code(byte_addr)` to fetch a 16-bit word.
- Word-addressed (PIC16): `lduw_code(word_addr * 2)`, then mask to 14 bits.
- Multi-word instructions: advance `dc->pc` once per 16-bit fetch.

### 9.3 Emitting TCG micro-ops

Every instruction maps to one or more TCG ops:

| Instruction pattern | TCG op |
|---|---|
| `ADD Rd, Rn` | `tcg_gen_add_i32(dst, src1, src2)` |
| `LDI Rd, k` | `tcg_gen_movi_i32(dst, k)` |
| `ST X, Rr` | `tcg_gen_qemu_st8(src, addr, mmu_idx)` |
| `LD Rd, X` | `tcg_gen_qemu_ld8u(dst, addr, mmu_idx)` |
| `RJMP k` | `tcg_gen_movi_i32(cpu_pc, target)` then end TB |
| conditional branch | `tcg_gen_brcondi_i32(cond, val, 0, label)` |
| helper call | `gen_helper_raise_exception(arg)` |

Use `tcg_temp_new_i32()` / `tcg_temp_free_i32()` for scratch temporaries.

### 9.4 Ending a translation block

After any branch or maximum-TB-size instruction:
```c
gen_tb_end(tb, instructions_translated);
tb->size = bytes_consumed;
```

### 9.5 Harvard data access

For architectures where data and program memory overlap in the physical address space,
bias all data addresses:
```c
#define DATA_BASE  0x800000u
TCGv addr = tcg_const_i32(DATA_BASE + io_offset);
tcg_gen_qemu_ld8u(dst, addr, 0);
tcg_temp_free_i32(addr);
```

---

## Step 10 — `renode/arch/{arch}/renode_{arch}_callbacks.c`

Bridge the four C callbacks to C# via the EXTERNAL_AS macro:

```c
#include "renode_imports.h"
#include "arch_callbacks.h"

EXTERNAL_AS(int32_t, tlib_find_best_interrupt, renode_find_best_interrupt)
EXTERNAL_AS(void, tlib_acknowledge_interrupt, renode_acknowledge_interrupt, int32_t)
EXTERNAL_AS(void, tlib_on_cpu_halted,     renode_on_cpu_halted)
EXTERNAL_AS(void, tlib_on_cpu_power_down, renode_on_cpu_power_down)
```

`EXTERNAL_AS(ret, c_name, cs_name [, arg_types…])` generates the glue that resolves
`c_name` to the C# method `cs_name` via the Renode native-import mechanism at runtime.

---

## Step 11 — `Cores/{ARCH}/{ARCH}.cs`

Create a `partial class` that inherits `TranslationCPU`:

```csharp
public partial class AVR : TranslationCPU
{
    public AVR(IMachine machine, string cpuType)
        : base(cpuType, machine, Endianess.LittleEndian) { }

    public override string Architecture     => "avr";
    public override string GDBArchitecture  => "avr";
    public override string[] AllLLVMTriples => new[] { "avr" };

    protected override Interrupt DecodeInterrupt(int number) => Interrupt.Hard;

    // --- Exports: C# methods callable from tlib ---
    [Export] private int  FindBestInterrupt()        => TryFindPendingInterrupt();
    [Export] private void AcknowledgeInterrupt(int n) => AcknowledgePendingInterrupt(n);
    [Export] private void OnCpuHalted()              { /* log or stall clock */ }
    [Export] private void OnCpuPowerDown()           { }

    // --- Imports: tlib functions callable from C# ---
    [Import] public Action<uint> TlibSetEntryPoint;
    [Import] public Action       TlibClearWfi;
    [Import] public Action       TlibSetWfi;

    public override Dictionary<ulong, string> ExceptionDescriptionsMap => new() {
        { 0, "RESET" }, { 1, "INT0" }, /* … */
    };
}
```

**Endianness:** pass `Endianess.BigEndian` for big-endian architectures (STM8, Sparc).
**GPIO inputs:** `new GPIO(1)` for one interrupt input line, `new GPIO(N)` for N.
**`DecodeInterrupt`:** maps interrupt wire number to `Interrupt.Hard` or `Interrupt.NMI`.

---

## Step 12 — `{ARCH}Registers.tt` and `{ARCH}Registers.cs`

### .tt (T4 template — optional, for auto-generation)

```
<#@ template language="C#" #>
<# const string CLASS_NAME = "AVR";
   string[] REGISTER_NAMES = new string[] { "R0", "R1", … "PC", "SREG", "SP" };
   string[] AFTER_WRITE_HOOKS = new string[] { "PC" };
#>
<#@ include file="../Common/RegistersTemplate.tt" #>
```

### .cs (hand-written or generated)

The `partial class` must provide:
- One `[Register]` property per architectural register with `get`/`set` calling
  `GetRegisterUnsafe((int)MyRegisters.X)` / `SetRegisterUnsafe(…)`.
- `GDBRegisters` property returning a `Dictionary<int, GDBRegisterDescriptor>` with
  the **bit width** (8, 16, 32) for each register — used by the GDB stub.
- `InitializeRegisters()` (can be empty).
- `AfterPCSet(uint value)` (can be empty — called whenever PC is written externally).

```csharp
protected override Dictionary<int, GDBRegisterDescriptor> GDBRegisters => new() {
    { (int)AVRRegisters.PC,   new GDBRegisterDescriptor((int)AVRRegisters.PC,   32, "PC",   "uint32", "general") },
    { (int)AVRRegisters.SREG, new GDBRegisterDescriptor((int)AVRRegisters.SREG,  8, "SREG", "uint8",  "general") },
};
```

---

## Step 13 — Peripheral Models

Each peripheral implements one of:
- `IBytePeripheral` (8-bit bus — PIC, AVR, STM8)
- `IWordPeripheral` (16-bit bus)
- `IDoubleWordPeripheral` (32-bit bus)
- `IKnownSize` — provides `long Size { get; }` (peripheral region length in bytes)

### UART skeleton

```csharp
public class AVR_USART : IUART, IBytePeripheral, IKnownSize
{
    public AVR_USART(IMachine machine) { IRQ = new GPIO(); rxQueue = new Queue<byte>(); }
    public byte ReadByte(long offset)  { /* read status/data regs */ return 0; }
    public void WriteByte(long offset, byte value) { /* handle TXREG → Transmit() */ }
    public void WriteChar(byte value)  { rxQueue.Enqueue(value); IRQ.Set(); IRQ.Unset(); }
    public event Action<byte> CharReceived;
    public uint BaudRate => ...; public Bits StopBits => Bits.One;
    public Parity ParityBit => Parity.None; public long Size => 0x07;
    public GPIO IRQ { get; }
    private readonly Queue<byte> rxQueue;
}
```

### GPIO skeleton (`IBytePeripheral`)

- `Connections = new GPIO[8]` — one per output pin.
- `ReadByte(0)` → return `portVal`.
- `WriteByte(0, v)` → write `portVal`, call `Connections[i].Set(…)` per output pin.
- `OnGPIO(int number, bool value)` → update `portVal` for input pins.

### Timer skeleton

Use `LimitTimer` from `Antmicro.Renode.Time`:
```csharp
timer = new LimitTimer(machine.ClockSource, frequency, this, "name",
    limit: 0xFF, enabled: false, eventEnabled: true);
timer.LimitReached += () => { irqFlag = true; IRQ.Set(); IRQ.Unset(); };
```
Write the prescaler divider to `timer.Divider` and enable via `timer.Enabled`.

---

## Step 14 — Platform Description File (`.repl`)

Create `platforms/cpus/{chipname}.repl`:

```
cpu: CPU.AVR @ sysbus
    cpuType: "atmega328p"

flash: Memory.MappedMemory @ sysbus 0x000000
    size: 0x8000

sram: Memory.MappedMemory @ sysbus 0x800100
    size: 0x800

usart0: UART.AVR_USART @ sysbus <0x8000C0, +0x7>
    -> cpu@0

portb: GPIOPort.AVR_GPIO @ sysbus <0x800023, +0x3>
```

Rules:
- CPU peripheral at `sysbus` with `cpuType` matching the string passed to `cpu_init()`.
- `<start, +size>` declares a peripheral region.
- `-> cpu@N` wires the peripheral's `IRQ` GPIO to CPU interrupt input `N`.
- For Harvard, place program memory at `0x000000` and data at the biased base
  (`PIC16_DATA_BASE`, `AVR_DATA_BASE`, etc.).

---

## Step 15 — Update `tlib/CMakeLists.txt`

Find the `set_property (CACHE TARGET_ARCH PROPERTY STRINGS …)` line and append
the new architecture name:

```cmake
set_property (CACHE TARGET_ARCH PROPERTY STRINGS
    i386 x86_64 arm arm-m arm64 sparc ppc ppc64 riscv riscv64 xtensa
    dspic33 c2000 avr stm8 pic16 pic18)
```

This makes the architecture selectable via `-DTARGET_ARCH=avr` at CMake configure time.

---

## Step 16 — Update `Infrastructure.csproj`

Two additions per new architecture:

### 1. EmbeddedResource (the compiled tlib `.so`)

```xml
<EmbeddedResource Include="Emulator/Cores/bin/$(Configuration)/lib/translate-avr-le.so">
  <LogicalName>Antmicro.Renode.translate-avr-le.so</LogicalName>
</EmbeddedResource>
```
Add `-le` suffix for little-endian, `-be` for big-endian (e.g. STM8 uses big-endian
byte order for multi-byte data but the `.so` filename convention still follows the
host build, so use `-le` on x86 build hosts).

### 2. Compile + None (the C# source files)

```xml
<ItemGroup>
  <Compile Include="Emulator/Cores/AVR/AVR.cs" />
  <Compile Include="Emulator/Cores/AVR/AVRRegisters.cs">
    <DependentUpon>AVRRegisters.tt</DependentUpon>
  </Compile>
</ItemGroup>
<ItemGroup>
  <None Include="Emulator/Cores/AVR/AVRRegisters.tt">
    <Generator>TextTemplatingFileGenerator</Generator>
    <LastGenOutput>AVRRegisters.cs</LastGenOutput>
  </None>
</ItemGroup>
```

---

## Step 17 — Build and Test Checklist

### Build tlib

```bash
cd src/Infrastructure/src/Emulator/Cores/tlib
mkdir build-avr && cd build-avr
cmake .. -DTARGET_ARCH=avr -DCMAKE_BUILD_TYPE=Release
make -j$(nproc)
cp libtranslate.so ../bin/Release/lib/translate-avr-le.so
```
Repeat for each new architecture.

### Build Renode

```bash
cd src/Infrastructure
./build.sh        # or: dotnet build Infrastructure.csproj
```

### Smoke test in Renode REPL

```python
# Load the platform
using sysbus
mach create
machine LoadPlatformDescription @platforms/cpus/atmega328p.repl

# Load a minimal binary (e.g. blink)
sysbus LoadBinary @blink.bin 0x0

# Run for a fixed number of instructions
cpu ExecutionMode SingleStep
cpu Step 100
cpu PC                  # should advance
```

### GDB connection test

```
(gdb) target remote :1234
(gdb) info registers   # should list all declared registers
(gdb) stepi            # single-step one instruction
```

### Common issues

| Symptom | Likely cause |
|---|---|
| `translate-avr-le.so` not found | `EmbeddedResource` entry missing or `.so` not copied |
| All registers show 0 | `get_reg_pointer_32` returns NULL for some indices |
| Crash on first instruction | `translate.c` missing `translate_init()` call or TCG global not initialized |
| Wrong PC after branch | Forgot to convert word address ↔ byte address |
| Peripheral not responding | `sysbus` address range doesn't match `IKnownSize.Size` |
| Interrupt never fires | `cpu_interrupts_enabled()` macro returns 0 / GIE never set |
| `do_interrupt` called but PC wrong | Vector address arithmetic incorrect |

---

## File Checklist per Architecture

```
tlib/arch/{arch}/
    cpu.h               ← CPUState, EXCP_*, cpu_interrupts_enabled()
    cpu_registers.h     ← register enum
    cpu_registers.c     ← get_reg_pointer_32() + CPU_REGISTER_ACCESSOR(32)
    arch_callbacks.h    ← 4 callback declarations
    arch_callbacks.c    ← DEFAULT_* stubs
    arch_exports.h      ← 3 export declarations
    arch_exports.c      ← EXC_VOID_N implementations
    helper.h            ← DEF_HELPER_* macros
    helper.c            ← cpu_init/cpu_reset/cpu_handle_mmu_fault
    op_helper.c         ← HELPER() bodies + do_interrupt() + process_interrupt()
    translate.c         ← translate_init() + disas_*_insn() + gen_intermediate_code*()

renode/arch/{arch}/
    renode_{arch}_callbacks.c   ← EXTERNAL_AS bridge

Cores/{ARCH}/
    {ARCH}.cs           ← TranslationCPU : partial class
    {ARCH}Registers.tt  ← T4 template (optional)
    {ARCH}Registers.cs  ← partial class with [Register] properties + GDBRegisters

Peripherals/UART/
    {ARCH}_USART.cs     ← IBytePeripheral, IUART, IKnownSize

Peripherals/GPIOPort/
    {ARCH}_GPIO.cs      ← IGPIOPort, IBytePeripheral, IKnownSize

Peripherals/Timers/
    {ARCH}_Timer.cs     ← ITimer, IBytePeripheral, IKnownSize

platforms/cpus/
    {chipname}.repl     ← Memory + CPU + peripherals
```

Total: **19 files** per architecture.

Infrastructure changes (done once per batch of new architectures):
- `tlib/CMakeLists.txt` — add arch name to STRINGS list
- `Infrastructure.csproj` — add `EmbeddedResource` + `Compile` + `None` entries
