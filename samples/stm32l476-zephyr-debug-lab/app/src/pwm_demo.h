#pragma once

#include <stdint.h>

int pwm_demo_init(void);
void pwm_demo_tick(void);
uint8_t pwm_demo_get_duty_percent(void);
