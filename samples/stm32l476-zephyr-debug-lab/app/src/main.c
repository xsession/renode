#include "app_trace.h"
#include "gpio_demo.h"
#include "pwm_demo.h"
#include "can_demo.h"
#include "fault_injection.h"

#include <zephyr/kernel.h>

#define HEARTBEAT_PERIOD K_MSEC(500)
#define PWM_PERIOD       K_MSEC(250)
#define CAN_PERIOD       K_MSEC(1000)

int main(void)
{
    app_trace_boot_banner();
    TRACE_EVENT("BOOT", "main entered");

    int ret;

    ret = gpio_demo_init();
    if (ret != 0) {
        TRACE_EVENT("BOOT", "gpio init failed ret=%d", ret);
    }

    ret = pwm_demo_init();
    if (ret != 0) {
        TRACE_EVENT("BOOT", "pwm init failed ret=%d", ret);
    }

    ret = can_demo_init();
    if (ret != 0) {
        TRACE_EVENT("BOOT", "can init failed ret=%d", ret);
    }

    fault_injection_init();

    int64_t next_heartbeat = k_uptime_get() + 500;
    int64_t next_pwm = k_uptime_get() + 250;
    int64_t next_can = k_uptime_get() + 1000;

    while (1) {
        int64_t now = k_uptime_get();

        if (now >= next_heartbeat) {
            next_heartbeat += 500;
            gpio_demo_tick();
        }

        if (now >= next_pwm) {
            next_pwm += 250;
            pwm_demo_tick();
        }

        if (now >= next_can) {
            next_can += 1000;
            can_demo_tick(
                pwm_demo_get_duty_percent(),
                gpio_demo_get_button_irq_count(),
                gpio_demo_get_led_state());
        }

        fault_injection_tick();
        k_sleep(K_MSEC(10));
    }

    return 0;
}
