#pragma once

#include <stdint.h>

void app_trace_boot_banner(void);
void app_trace_event(const char *module, const char *fmt, ...);

#define TRACE_EVENT(module, fmt, ...) \
    app_trace_event(module, fmt, ##__VA_ARGS__)
