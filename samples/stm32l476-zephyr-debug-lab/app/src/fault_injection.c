#include "fault_injection.h"
#include "app_trace.h"

#include <zephyr/kernel.h>

/*
 * GDB-callable hooks for laboratory exercises.
 * Example in GDB:
 *   call debug_force_trace_marker()
 *   call debug_simulate_button_event()
 */

void debug_force_trace_marker(void)
{
    TRACE_EVENT("DBG", "manual trace marker from GDB");
}

void debug_simulate_button_event(void)
{
    TRACE_EVENT("DBG", "software-injected button event placeholder");
}

void debug_halt_here(void)
{
    TRACE_EVENT("DBG", "breakpoint hook entered");
}

void fault_injection_init(void)
{
    TRACE_EVENT("DBG", "fault injection hooks ready");
}

void fault_injection_tick(void)
{
    /* Intentionally empty. Students add exercises here. */
}
