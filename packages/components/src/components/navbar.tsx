import { Anchor, Text, XStack, YStack } from "tamagui";

import type { NavbarProps } from "./navbar.props";

const defaultSections = [
  { title: "Dashboard", href: "/" },
  { title: "Setup", href: "/setup" },
  { title: "API keys", href: "/keys" },
];

function isActive(href: string, path: string) {
  return href === "/" ? path === "/" : path.startsWith(href);
}

export function Navbar({ title = "OpenStar", sections = defaultSections }: NavbarProps) {
  const path = typeof window !== "undefined" ? window.location.pathname : "/";

  return (
    <XStack
      render="header"
      width="100%"
      borderBottomWidth={1}
      borderColor="$borderColor"
      backgroundColor="$background"
      paddingHorizontal="$5"
      paddingVertical="$3"
      style={{ position: "sticky", top: 0, zIndex: 10, backdropFilter: "blur(8px)" }}
    >
      <XStack
        render="nav"
        marginHorizontal="auto"
        width="100%"
        maxWidth={1024}
        alignItems="center"
        justifyContent="space-between"
        gap="$4"
      >
        <Anchor href="/" textDecorationLine="none">
          <XStack alignItems="center" gap="$2.5">
            <YStack
              width={30}
              height={30}
              borderRadius="$3"
              alignItems="center"
              justifyContent="center"
              style={{ background: "linear-gradient(135deg, #6366f1, #8b5cf6)" }}
            >
              <Text fontSize={16} fontWeight="900" color="#fff" lineHeight={16}>
                ✦
              </Text>
            </YStack>
            <Text fontSize={18} fontWeight="800" color="$color12" letterSpacing={-0.2}>
              {title}
            </Text>
          </XStack>
        </Anchor>

        <XStack alignItems="center" gap="$1.5">
          {sections.map((section) => {
            const active = isActive(section.href, path);
            return (
              <Anchor
                key={section.href}
                href={section.href}
                textDecorationLine="none"
                paddingHorizontal="$3"
                paddingVertical="$2"
                borderRadius="$4"
                fontSize={14}
                fontWeight="600"
                color={active ? "$color12" : "$color11"}
                backgroundColor={active ? "$color4" : "transparent"}
                hoverStyle={{ backgroundColor: "$color3", color: "$color12" }}
              >
                {section.title}
              </Anchor>
            );
          })}
        </XStack>
      </XStack>
    </XStack>
  );
}
