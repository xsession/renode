#pragma once

#include <stdint.h>

int can_demo_init(void);
void can_demo_tick(uint8_t duty_percent, uint32_t button_count, int led_state);
