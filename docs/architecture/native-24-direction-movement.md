# 24-direction movement

## Wire discriminator

Packet 10005 carries a 32-bit direction field at +4 and a 32-bit
selector at +12. The receiver compares **only the selector low byte**
with 9. Matching selectors use the 24-entry displacement table and
normalize the direction low byte modulo 24; other selectors use the
eight-entry table and modulo 8. The complete selector and packed
direction remain part of the original packet and are never rewritten.

The displacement tables and operand-width evidence are recorded in
`20_notes/Network/15_msgwalk_10005_direction_index_order.md` and
`20_notes/Server/movement.md`. A constructed selector `0x00000109`
with packed direction `0x17` selects index 23, displacement (+1,+2).
An ordinary `0x00000101` selector uses eight-direction displacement;
its selector low byte selects the running action family. These
constructed discriminators are not independently captured traffic.

## Authenticated movement policy

The server validates the authenticated character identity, uses its
own partition as the position authority and preserves the packet's
full selector, packed direction, movement tick and map-state fields
when relaying a committed ordinary step. Non-mounted selectors retain
their established ground movement path, subject to pacing, ownership
and composed collision.

Riding authority and vigor are not implemented. The world runtime
therefore rejects **every selector whose low byte is 9** before
enqueueing a position change, including upper-byte aliases. The
session returns action 108 with the unchanged authoritative tile;
neither the sender nor observers receive a movement echo. The
partition still retains its 24-entry displacement and intermediate
collision checks for a future authorized riding flow.

## Remaining acceptance

No mounted move is authorized by this decoder correction. Riding
status, vigor, interactive client comparisons and mounted movement
timing remain separate work. See
[ground walking](ground-walking.md) and
[movement eligibility](authoritative-movement-eligibility.md).
