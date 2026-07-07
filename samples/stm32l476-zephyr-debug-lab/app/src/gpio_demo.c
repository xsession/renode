#include "gpio_demo.h"
#include "app_trace.h"

#include <zephyr/kernel.h>
#include <zephyr/device.h>
#include <zephyr/drivers/gpio.h>

#define LED0_NODE DT_ALIAS(led0)
#define SW0_NODE  DT_ALIAS(sw0)

#if !DT_NODE_HAS_STATUS(LED0_NODE, okay)
#error "Unsupported board: led0 devicetree alias is not defined"
#endif

static const struct gpio_dt_spec led = GPIO_DT_SPEC_GET(LED0_NODE, gpios);

#if DT_NODE_HAS_STATUS(SW0_NODE, okay)
static const struct gpio_dt_spec button = GPIO_DT_SPEC_GET(SW0_NODE, gpios);
static struct gpio_callback button_cb_data;
#endif

static volatile uint32_t button_irq_count;
static int led_state;

#if DT_NODE_HAS_STATUS(SW0_NODE, okay)
static void button_pressed(const struct device *dev, struct gpio_callback *cb, uint32_t pins)
{
    ARG_UNUSED(dev);
    ARG_UNUSED(cb);
    ARG_UNUSED(pins);

    button_irq_count++;
    TRACE_EVENT("GPIO", "button IRQ count=%u", button_irq_count);
}
#endif

int gpio_demo_init(void)
{
    int ret;

    if (!gpio_is_ready_dt(&led)) {
        TRACE_EVENT("GPIO", "LED device not ready");
        return -ENODEV;
    }

    ret = gpio_pin_configure_dt(&led, GPIO_OUTPUT_INACTIVE);
    if (ret < 0) {
        TRACE_EVENT("GPIO", "LED configure failed ret=%d", ret);
        return ret;
    }

#if DT_NODE_HAS_STATUS(SW0_NODE, okay)
    if (!gpio_is_ready_dt(&button)) {
        TRACE_EVENT("GPIO", "button device not ready");
        return -ENODEV;
    }

    ret = gpio_pin_configure_dt(&button, GPIO_INPUT);
    if (ret < 0) {
        TRACE_EVENT("GPIO", "button configure failed ret=%d", ret);
        return ret;
    }

    ret = gpio_pin_interrupt_configure_dt(&button, GPIO_INT_EDGE_TO_ACTIVE);
    if (ret < 0) {
        TRACE_EVENT("GPIO", "button interrupt configure failed ret=%d", ret);
        return ret;
    }

    gpio_init_callback(&button_cb_data, button_pressed, BIT(button.pin));
    gpio_add_callback(button.port, &button_cb_data);
#endif

    TRACE_EVENT("GPIO", "initialized LED pin=%u", led.pin);
    return 0;
}

void gpio_demo_tick(void)
{
    led_state = !led_state;
    gpio_pin_set_dt(&led, led_state);
    TRACE_EVENT("GPIO", "heartbeat led=%d button_irq_count=%u", led_state, button_irq_count);
}

uint32_t gpio_demo_get_button_irq_count(void)
{
    return button_irq_count;
}

int gpio_demo_get_led_state(void)
{
    return led_state;
}
