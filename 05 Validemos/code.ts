@Component
@AllArgsConstructor
public class ChannelInfoEpgValidator implements EpgValidator {

    @Override
    public void validate(final OneDataTv tv, final ChannelInfo channelInfo) {
        final OneDataChannel channel = Optional
            .ofNullable(tv.getChannel())
            .orElseThrow(() -> new InvalidEpgException("No channel data found."));
        if (!channelInfo.getId().equals(channel.getId())) {
            throw new InvalidEpgException(String.format(
                "Invalid channel id %s (expected %s).", channel.getId(), channelInfo.getId()));
        }
    }
}


public static void checkEPGScheduleForOverlaps(final List<AgileTvEPGProgramme> programs) {
    List<AgileTvEPGProgramme> sortedPrograms = programs.stream()
        .sorted(Comparator.comparing(AgileTvEPGProgramme::getStart))
        .collect(Collectors.toList());

    for (int i = 0; i < sortedPrograms.size(); i++) {
        if (i == sortedPrograms.size() - 1) {
            break;
        }
        AgileTvEPGProgramme currentProgram = sortedPrograms.get(i);
        AgileTvEPGProgramme nextProgram = sortedPrograms.get(i + 1);
        if (!currentProgram.getStop().equals(nextProgram.getStart())) {
            DateTimeFormatter formatter = DateTimeFormatter.ofPattern(YYYY_MM_DD_HHMMSS_Z);
            LocalDateTime currentProgramStopTime = LocalDateTime.parse(currentProgram.getStop(), formatter);
            LocalDateTime nextProgramStartTime = LocalDateTime.parse(nextProgram.getStart(), formatter);

            if (currentProgramStopTime.isAfter(nextProgramStartTime)) {
                throw new RuntimeException(String.format(
                    "Conflicting times with emission %s with stop time [%s] and next emission %s with start time [%s]",
                    currentProgram.getId(), currentProgram.getStop(), nextProgram.getId(), nextProgram.getStart()));
            }
        }
    }
}

public interface EpgValidator {
    EpgValidator[] validators = new EpgValidator[]{
        new ChannelInfoEpgValidator(),
        new RequiredFieldsEpgValidator(),
        new ScheduleOverlapsEpgValidator()
    };

    void validate(OneDataTv tv, ChannelInfo channelInfo) throws InvalidEpgException;

    static void applyAllValidators(final OneDataTv tv, final ChannelInfo channelInfo) {
        Arrays.stream(validators).forEach(epgValidator -> epgValidator.validate(tv, channelInfo));
    }
}
