#include "can_demo.h"
#include "app_trace.h"

#include <zephyr/kernel.h>
#include <zephyr/device.h>
#include <zephyr/drivers/can.h>
#include <zephyr/sys/byteorder.h>

/*
 * Default tries to find a CAN controller from a common devicetree alias.
 * If your board overlay names it differently, set zephyr,canbus chosen node or
 * adapt CAN_DEV_NODE below.
 */
#if DT_NODE_HAS_STATUS(DT_CHOSEN(zephyr_canbus), okay)
#define CAN_DEV_NODE DT_CHOSEN(zephyr_canbus)
#elif DT_NODE_HAS_STATUS(DT_ALIAS(can0), okay)
#define CAN_DEV_NODE DT_ALIAS(can0)
#else
#define CAN_DEV_NODE DT_INVALID_NODE
#endif

#if DT_NODE_HAS_STATUS(CAN_DEV_NODE, okay)
static const struct device *const can_dev = DEVICE_DT_GET(CAN_DEV_NODE);
#else
static const struct device *const can_dev = NULL;
#endif

static struct k_work_delayable can_rx_rearm_work;
static int rx_filter_id = -1;

static void can_rx_callback(const struct device *dev, struct can_frame *frame, void *user_data)
{
    ARG_UNUSED(dev);
    ARG_UNUSED(user_data);

    TRACE_EVENT("CAN", "RX id=0x%03x dlc=%u data=%02x %02x %02x %02x %02x %02x %02x %02x",
                frame->id, frame->dlc,
                frame->data[0], frame->data[1], frame->data[2], frame->data[3],
                frame->data[4], frame->data[5], frame->data[6], frame->data[7]);
}

static void can_rx_rearm_handler(struct k_work *work)
{
    ARG_UNUSED(work);
    /* Reserved for future advanced exercises. */
}

int can_demo_init(void)
{
    k_work_init_delayable(&can_rx_rearm_work, can_rx_rearm_handler);

    if (can_dev == NULL) {
        TRACE_EVENT("CAN", "no CAN device in devicetree; running CAN demo in log-only mode");
        return 0;
    }

    if (!device_is_ready(can_dev)) {
        TRACE_EVENT("CAN", "device not ready");
        return -ENODEV;
    }

#ifdef CONFIG_CAN_LOOPBACK_MODE
    int ret = can_set_mode(can_dev, CAN_MODE_LOOPBACK);
    if (ret != 0) {
        TRACE_EVENT("CAN", "loopback mode failed ret=%d; continue anyway", ret);
    } else {
        TRACE_EVENT("CAN", "loopback mode enabled");
    }
#endif

    struct can_filter filter = {
        .flags = 0U,
        .id = 0x123,
        .mask = CAN_STD_ID_MASK,
    };

    rx_filter_id = can_add_rx_filter(can_dev, can_rx_callback, NULL, &filter);
    if (rx_filter_id < 0) {
        TRACE_EVENT("CAN", "rx filter failed ret=%d", rx_filter_id);
        return rx_filter_id;
    }

    int ret = can_start(can_dev);
    if (ret != 0) {
        TRACE_EVENT("CAN", "start failed ret=%d", ret);
        return ret;
    }

    TRACE_EVENT("CAN", "initialized filter=%d", rx_filter_id);
    return 0;
}

void can_demo_tick(uint8_t duty_percent, uint32_t button_count, int led_state)
{
    struct can_frame frame = {0};

    frame.id = 0x123;
    frame.dlc = 8;
    frame.data[0] = 0xA5;
    frame.data[1] = duty_percent;
    frame.data[2] = (uint8_t)(button_count & 0xFFU);
    frame.data[3] = led_state ? 1U : 0U;
    frame.data[4] = 0x11;
    frame.data[5] = 0x22;
    frame.data[6] = 0x33;
    frame.data[7] = 0x44;

    if (can_dev == NULL || !device_is_ready(can_dev)) {
        TRACE_EVENT("CAN", "TX simulated id=0x%03x duty=%u button=%u led=%d",
                    frame.id, duty_percent, button_count, led_state);
        return;
    }

    int ret = can_send(can_dev, &frame, K_MSEC(100), NULL, NULL);
    if (ret != 0) {
        TRACE_EVENT("CAN", "TX failed ret=%d id=0x%03x", ret, frame.id);
    } else {
        TRACE_EVENT("CAN", "TX id=0x%03x duty=%u button=%u led=%d",
                    frame.id, duty_percent, button_count, led_state);
    }
}
