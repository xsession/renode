#pragma once

#include <stdint.h>

int gpio_demo_init(void);
void gpio_demo_tick(void);
uint32_t gpio_demo_get_button_irq_count(void);
int gpio_demo_get_led_state(void);
