#include "app_trace.h"

#include <zephyr/kernel.h>
#include <zephyr/sys/printk.h>

#include <stdarg.h>
#include <stdio.h>

void app_trace_boot_banner(void)
{
    printk("\n");
    printk("============================================================\n");
    printk(" STM32L476 Zephyr Debug Lab\n");
    printk(" GPIO + PWM + CAN + OpenOCD + Renode trace workflow\n");
    printk("============================================================\n");
}

void app_trace_event(const char *module, const char *fmt, ...)
{
    char buf[192];
    va_list args;

    va_start(args, fmt);
    vsnprintk(buf, sizeof(buf), fmt, args);
    va_end(args);

    printk("[%010lld ms] %-8s %s\n", k_uptime_get(), module, buf);
}
