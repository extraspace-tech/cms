import { z } from 'zod'

const inRange = (value: string, min: number, max: number) => Number.isFinite(Number(value)) && Number(value) >= min && Number(value) <= max
const int32 = 2147483647

/** Number inputs stay text in the form (an empty box is ''); the page converts them when it builds the body. */
export function optionalNumber(min: number, max: number) {
  return z.string().trim().refine((value) => value === '' || inRange(value, min, max), `Enter a number from ${min} to ${max}.`)
}

export function requiredNumber(min: number, max: number) {
  return z.string().trim().min(1, 'Required.').refine((value) => inRange(value, min, max), `Enter a number from ${min} to ${max}.`)
}

/** Bound to the server's `int`: a larger value would be refused by its binding, not by a field check. */
export function wholeNumber() {
  return z.string().trim()
    .refine((value) => value === '' || Number.isInteger(Number(value)), 'Enter a whole number.')
    .refine((value) => value === '' || inRange(value, -int32, int32), `Enter a number from ${-int32} to ${int32}.`)
}

/** The server refuses a third decimal rather than round it. */
export const atMostTwoDecimals = (value: string) => value === '' || Number(value) === Math.round(Number(value) * 100) / 100
