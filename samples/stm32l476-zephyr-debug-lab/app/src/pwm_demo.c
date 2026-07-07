#include "pwm_demo.h"
#include "app_trace.h"

#include <zephyr/kernel.h>
#include <zephyr/device.h>
#include <zephyr/drivers/pwm.h>

#define PWM_LED0_NODE DT_ALIAS(pwm_led0)

#if DT_NODE_HAS_STATUS(PWM_LED0_NODE, okay)
static const struct pwm_dt_spec pwm_led = PWM_DT_SPEC_GET(PWM_LED0_NODE);
#endif

static uint8_t duty_percent = 10;
static const uint32_t pwm_period_ns = 1000000U; /* 1 kHz */

static int pwm_apply(uint8_t duty)
{
#if DT_NODE_HAS_STATUS(PWM_LED0_NODE, okay)
    uint32_t pulse_ns = (pwm_period_ns * duty) / 100U;
    int ret = pwm_set_dt(&pwm_led, pwm_period_ns, pulse_ns);
    if (ret < 0) {
        TRACE_EVENT("PWM", "set failed ret=%d", ret);
    }
    return ret;
#else
    TRACE_EVENT("PWM", "pwm_led0 alias missing; simulated duty=%u", duty);
    return 0;
#endif
}

int pwm_demo_init(void)
{
#if DT_NODE_HAS_STATUS(PWM_LED0_NODE, okay)
    if (!pwm_is_ready_dt(&pwm_led)) {
        TRACE_EVENT("PWM", "device not ready");
        return -ENODEV;
    }
#endif

    int ret = pwm_apply(duty_percent);
    TRACE_EVENT("PWM", "initialized period_ns=%u duty=%u", pwm_period_ns, duty_percent);
    return ret;
}

void pwm_demo_tick(void)
{
    duty_percent += 10;
    if (duty_percent > 90) {
        duty_percent = 10;
    }

    pwm_apply(duty_percent);
    TRACE_EVENT("PWM", "duty=%u%%", duty_percent);
}

uint8_t pwm_demo_get_duty_percent(void)
{
    return duty_percent;
}
