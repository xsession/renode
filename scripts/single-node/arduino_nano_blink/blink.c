/*
 * Bare-metal blink for ATmega328P (Arduino Nano).
 * Toggles PB5 (LED on pin 13) using Timer1 compare-match delay.
 * Compiled with: avr-gcc -mmcu=atmega328p -Os -o blink.elf blink.c
 */
#include <avr/io.h>

static void delay_ms(unsigned int ms)
{
    /* Busy-wait loop.  At 16 MHz each iteration ~4 cycles ~= 0.25 us.
       4000 inner iterations ≈ 1 ms.  Good enough for blink. */
    while (ms--) {
        volatile unsigned int i;
        for (i = 0; i < 4000; i++)
            ;
    }
}

int main(void)
{
    /* PB5 = output (Arduino Nano LED) */
    DDRB |= (1 << DDB5);

    while (1) {
        PORTB |=  (1 << PORTB5);   /* LED ON  */
        delay_ms(500);
        PORTB &= ~(1 << PORTB5);   /* LED OFF */
        delay_ms(500);
    }
    return 0;
}
